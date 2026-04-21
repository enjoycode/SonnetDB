using System.Buffers;
using System.IO.Hashing;
using System.Runtime.InteropServices;
using System.Text;
using SonnetDB.IO;
using SonnetDB.Storage.Format;

namespace SonnetDB.Catalog;

/// <summary>
/// Measurement schema 文件（<c>measurements.tslschema</c>）的序列化与反序列化器。
/// <para>
/// 物理布局（二进制，little-endian）：
/// <code>
/// MeasurementSchemaHeader (32B)
///   Magic = "SDBMEAv1"      (8B)
///   FormatVersion = 1       (4B)
///   HeaderSize = 32         (4B)
///   MeasurementCount        (4B)
///   Reserved                (12B)
///
/// MeasurementSchema[N]（每条变长）
///   uint16 NameUtf8Length              (2B)
///   byte[] NameUtf8                    变长
///   long   CreatedAtUtcTicks           (8B)
///   uint16 ColumnCount                 (2B)
///   Column[ColumnCount]:
///     uint16 ColumnNameUtf8Length      (2B)
///     byte[] ColumnNameUtf8            变长
///     byte   Role                      (1B)  0=Tag, 1=Field
///     byte   DataType                  (1B)  SonnetDB.Storage.Format.FieldType
///
/// MeasurementSchemaFooter (16B)
///   Crc32                              (4B)  整个 MeasurementSchema[] 区域的 CRC32
///   Magic = "SDBMEAv1"                 (8B)
///   Reserved                           (4B)
/// </code>
/// </para>
/// <para>写入策略：临时文件 + 原子 rename（崩溃安全）。</para>
/// </summary>
public static class MeasurementSchemaCodec
{
    /// <summary>schema 文件名。</summary>
    public const string FileName = "measurements.tslschema";

    private static readonly byte[] _magic = "SDBMEAv1"u8.ToArray();
    private static readonly Encoding _utf8 = Encoding.UTF8;

    private const int _formatVersion = 1;
    private const int _headerSize = 32;
    private const int _footerSize = 16;

    /// <summary>
    /// 从指定路径加载 schema 列表；文件不存在时返回空列表。
    /// </summary>
    /// <param name="path">schema 文件完整路径。</param>
    /// <returns>schema 只读列表；文件不存在时返回空列表。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> 为 null。</exception>
    /// <exception cref="InvalidDataException">Magic / Version / Crc32 校验失败时抛出。</exception>
    public static IReadOnlyList<MeasurementSchema> Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!File.Exists(path))
            return [];

        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Load(fs);
    }

    /// <summary>
    /// 把 schema 列表持久化到指定路径（临时文件 + 原子 rename + fsync）。
    /// </summary>
    /// <param name="path">目标文件完整路径。</param>
    /// <param name="schemas">要持久化的 schema 列表。</param>
    /// <param name="tempSuffix">临时文件后缀（默认 <c>".tmp"</c>）。</param>
    /// <exception cref="ArgumentNullException">任何参数为 null。</exception>
    public static void Save(string path, IReadOnlyList<MeasurementSchema> schemas, string tempSuffix = ".tmp")
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(schemas);
        ArgumentNullException.ThrowIfNull(tempSuffix);

        string tmpPath = path + tempSuffix;

        using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var bs = new BufferedStream(fs, 65536))
        {
            Save(schemas, bs);
            bs.Flush();
            fs.Flush(true);
        }

        File.Move(tmpPath, path, overwrite: true);
    }

    // ── 私有实现 ──────────────────────────────────────────────────────────────

    private static IReadOnlyList<MeasurementSchema> Load(Stream source)
    {
        byte[] headerBuf = ArrayPool<byte>.Shared.Rent(_headerSize);
        try
        {
            int read = ReadExact(source, headerBuf, 0, _headerSize);
            if (read < _headerSize)
                throw new InvalidDataException("MeasurementSchema: header is truncated.");

            var headerReader = new SpanReader(headerBuf.AsSpan(0, _headerSize));
            ReadOnlySpan<byte> magic = headerReader.ReadBytes(8);
            if (!magic.SequenceEqual(_magic))
                throw new InvalidDataException("MeasurementSchema: invalid magic in header.");

            int version = headerReader.ReadInt32();
            if (version != _formatVersion)
                throw new InvalidDataException($"MeasurementSchema: unsupported format version {version}.");

            int hSize = headerReader.ReadInt32();
            if (hSize != _headerSize)
                throw new InvalidDataException($"MeasurementSchema: unexpected header size {hSize}.");

            int measurementCount = headerReader.ReadInt32();
            if (measurementCount < 0)
                throw new InvalidDataException("MeasurementSchema: negative measurement count.");

            var crcHasher = new Crc32();
            var result = new List<MeasurementSchema>(measurementCount);

            for (int i = 0; i < measurementCount; i++)
                result.Add(ReadMeasurement(source, crcHasher, i));

            // Footer
            byte[] footerBuf = ArrayPool<byte>.Shared.Rent(_footerSize);
            try
            {
                int footerRead = ReadExact(source, footerBuf, 0, _footerSize);
                if (footerRead < _footerSize)
                    throw new InvalidDataException("MeasurementSchema: footer is truncated.");

                uint storedCrc = MemoryMarshal.Read<uint>(footerBuf.AsSpan(0, 4));
                ReadOnlySpan<byte> footerMagic = footerBuf.AsSpan(4, 8);
                if (!footerMagic.SequenceEqual(_magic))
                    throw new InvalidDataException("MeasurementSchema: invalid magic in footer.");

                uint computedCrc = crcHasher.GetCurrentHashAsUInt32();
                if (computedCrc != storedCrc)
                    throw new InvalidDataException(
                        $"MeasurementSchema: CRC32 mismatch (expected 0x{storedCrc:X8}, got 0x{computedCrc:X8}).");
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(footerBuf);
            }

            return result.AsReadOnly();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(headerBuf);
        }
    }

    private static MeasurementSchema ReadMeasurement(Stream source, Crc32 crc, int index)
    {
        // Name: uint16 length + bytes
        string name = ReadString(source, crc, fieldDescription: $"measurement {index} name");

        // CreatedAt: long (8B)
        Span<byte> createdBuf = stackalloc byte[8];
        ReadExactSpan(source, createdBuf, $"measurement {index} createdAt");
        crc.Append(createdBuf);
        long createdAt = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(createdBuf);

        // ColumnCount: uint16 (2B)
        Span<byte> countBuf = stackalloc byte[2];
        ReadExactSpan(source, countBuf, $"measurement {index} columnCount");
        crc.Append(countBuf);
        int columnCount = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(countBuf);

        var columns = new List<MeasurementColumn>(columnCount);
        Span<byte> roleAndType = stackalloc byte[2];
        for (int c = 0; c < columnCount; c++)
        {
            string colName = ReadString(source, crc, fieldDescription: $"measurement {index} column {c} name");

            ReadExactSpan(source, roleAndType, $"measurement {index} column {c} role/type");
            crc.Append(roleAndType);

            byte roleByte = roleAndType[0];
            byte typeByte = roleAndType[1];

            if (roleByte > 1)
                throw new InvalidDataException(
                    $"MeasurementSchema: invalid column role {roleByte} (measurement '{name}', column {c}).");

            var role = (MeasurementColumnRole)roleByte;
            var dataType = (FieldType)typeByte;
            if (!Enum.IsDefined(dataType))
                throw new InvalidDataException(
                    $"MeasurementSchema: invalid column data type {typeByte} (measurement '{name}', column '{colName}').");

            columns.Add(new MeasurementColumn(colName, role, dataType));
        }

        return MeasurementSchema.Create(name, columns, createdAt);
    }

    private static void Save(IReadOnlyList<MeasurementSchema> schemas, Stream destination)
    {
        // Header
        byte[] headerBuf = ArrayPool<byte>.Shared.Rent(_headerSize);
        try
        {
            headerBuf.AsSpan(0, _headerSize).Clear();
            var writer = new SpanWriter(headerBuf.AsSpan(0, _headerSize));
            writer.WriteBytes(_magic);
            writer.WriteInt32(_formatVersion);
            writer.WriteInt32(_headerSize);
            writer.WriteInt32(schemas.Count);
            destination.Write(headerBuf, 0, _headerSize);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(headerBuf);
        }

        var crcHasher = new Crc32();

        foreach (var schema in schemas)
            WriteMeasurement(destination, schema, crcHasher);

        // Footer
        uint crc = crcHasher.GetCurrentHashAsUInt32();
        byte[] footerBuf = ArrayPool<byte>.Shared.Rent(_footerSize);
        try
        {
            footerBuf.AsSpan(0, _footerSize).Clear();
            var writer = new SpanWriter(footerBuf.AsSpan(0, _footerSize));
            writer.WriteUInt32(crc);
            writer.WriteBytes(_magic);
            destination.Write(footerBuf, 0, _footerSize);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(footerBuf);
        }
    }

    private static void WriteMeasurement(Stream destination, MeasurementSchema schema, Crc32 crc)
    {
        int nameLen = _utf8.GetByteCount(schema.Name);
        if (nameLen > ushort.MaxValue)
            throw new InvalidDataException($"Measurement '{schema.Name}' 名称过长（{nameLen} 字节，最大 {ushort.MaxValue}）。");

        // Compute total size for measurement record
        int totalSize = 2 + nameLen + 8 + 2; // nameLen + name + createdAt + columnCount

        var columnSizes = new int[schema.Columns.Count];
        for (int i = 0; i < schema.Columns.Count; i++)
        {
            int colNameLen = _utf8.GetByteCount(schema.Columns[i].Name);
            if (colNameLen > ushort.MaxValue)
                throw new InvalidDataException(
                    $"Column '{schema.Columns[i].Name}' 名称过长（{colNameLen} 字节，最大 {ushort.MaxValue}）。");
            columnSizes[i] = 2 + colNameLen + 1 + 1; // nameLen + name + role + type
            totalSize += columnSizes[i];
        }

        byte[] buf = ArrayPool<byte>.Shared.Rent(totalSize);
        try
        {
            buf.AsSpan(0, totalSize).Clear();
            var writer = new SpanWriter(buf.AsSpan(0, totalSize));

            writer.WriteUInt16((ushort)nameLen);
            int written = _utf8.GetBytes(schema.Name, writer.FreeSpan);
            writer.Advance(written);

            writer.WriteInt64(schema.CreatedAtUtcTicks);
            writer.WriteUInt16((ushort)schema.Columns.Count);

            for (int i = 0; i < schema.Columns.Count; i++)
            {
                var col = schema.Columns[i];
                int colNameLen = columnSizes[i] - 4; // 2(len) + 1(role) + 1(type) = 4 fixed
                writer.WriteUInt16((ushort)colNameLen);
                int colWritten = _utf8.GetBytes(col.Name, writer.FreeSpan);
                writer.Advance(colWritten);
                writer.WriteByte((byte)col.Role);
                writer.WriteByte((byte)col.DataType);
            }

            crc.Append(buf.AsSpan(0, totalSize));
            destination.Write(buf, 0, totalSize);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }

    private static string ReadString(Stream source, Crc32 crc, string fieldDescription)
    {
        Span<byte> lenBuf = stackalloc byte[2];
        ReadExactSpan(source, lenBuf, fieldDescription + " length");
        crc.Append(lenBuf);
        int len = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(lenBuf);
        if (len == 0)
            return string.Empty;

        byte[] buf = ArrayPool<byte>.Shared.Rent(len);
        try
        {
            int read = ReadExact(source, buf, 0, len);
            if (read < len)
                throw new InvalidDataException($"MeasurementSchema: {fieldDescription} is truncated.");
            crc.Append(buf.AsSpan(0, len));
            return _utf8.GetString(buf, 0, len);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }

    private static void ReadExactSpan(Stream source, Span<byte> buffer, string fieldDescription)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = source.Read(buffer[total..]);
            if (read == 0)
                throw new InvalidDataException($"MeasurementSchema: {fieldDescription} is truncated.");
            total += read;
        }
    }

    private static int ReadExact(Stream stream, byte[] buffer, int offset, int count)
    {
        int total = 0;
        while (total < count)
        {
            int read = stream.Read(buffer, offset + total, count - total);
            if (read == 0)
                break;
            total += read;
        }
        return total;
    }
}
