using System.Text;
using SonnetDB.IO;
using SonnetDB.Model;
using SonnetDB.Storage.Format;

namespace SonnetDB.Wal;

/// <summary>
/// WAL 载荷编解码器，负责 4 种记录类型的 payload 序列化与反序列化。
/// </summary>
internal static class WalPayloadCodec
{
    private static readonly Encoding _utf8 = Encoding.UTF8;

    // ─────────────────────────── 写入 ──────────────────────────────────────

    /// <summary>
    /// 计算 WritePoint 载荷的字节大小。
    /// </summary>
    internal static int MeasureWritePoint(string fieldName, FieldValue value)
    {
        int fieldNameBytes = _utf8.GetByteCount(fieldName);
        int valueBytes = value.Type switch
        {
            FieldType.Float64 => 8,
            FieldType.Int64 => 8,
            FieldType.Boolean => 1,
            FieldType.String => _utf8.GetByteCount(value.AsString()),
            _ => throw new ArgumentOutOfRangeException(nameof(value), $"Unsupported FieldType: {value.Type}"),
        };
        // SeriesId(8) + PointTs(8) + FieldType(1) + Padding(3) + FieldNameLen(4) + FieldNameBytes + ValueLen(4) + ValueBytes
        return 8 + 8 + 1 + 3 + 4 + fieldNameBytes + 4 + valueBytes;
    }

    /// <summary>
    /// 计算 CreateSeries 载荷的字节大小。
    /// </summary>
    internal static int MeasureCreateSeries(string measurement, IReadOnlyDictionary<string, string> tags)
    {
        int size = 8 + 4 + _utf8.GetByteCount(measurement) + 4; // SeriesId + MeasLen + MeasBytes + TagCount
        foreach (var kv in tags)
            size += 4 + _utf8.GetByteCount(kv.Key) + 4 + _utf8.GetByteCount(kv.Value);
        return size;
    }

    /// <summary>
    /// 将 WritePoint 载荷写入 <see cref="SpanWriter"/>。
    /// </summary>
    internal static void WriteWritePointPayload(
        ref SpanWriter writer,
        ulong seriesId,
        long pointTimestamp,
        string fieldName,
        FieldValue value)
    {
        writer.WriteUInt64(seriesId);
        writer.WriteInt64(pointTimestamp);
        writer.WriteByte((byte)value.Type);
        writer.WriteByte(0); // padding
        writer.WriteByte(0); // padding
        writer.WriteByte(0); // padding

        int fieldNameBytes = _utf8.GetByteCount(fieldName);
        writer.WriteInt32(fieldNameBytes);
        int written = _utf8.GetBytes(fieldName, writer.FreeSpan);
        writer.Advance(written);

        switch (value.Type)
        {
            case FieldType.Float64:
                writer.WriteInt32(0);
                writer.WriteDouble(value.AsDouble());
                break;
            case FieldType.Int64:
                writer.WriteInt32(0);
                writer.WriteInt64(value.AsLong());
                break;
            case FieldType.Boolean:
                writer.WriteInt32(0);
                writer.WriteByte(value.AsBool() ? (byte)1 : (byte)0);
                break;
            case FieldType.String:
                {
                    string str = value.AsString();
                    int strBytes = _utf8.GetByteCount(str);
                    writer.WriteInt32(strBytes);
                    int strWritten = _utf8.GetBytes(str, writer.FreeSpan);
                    writer.Advance(strWritten);
                    break;
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(value), $"Unsupported FieldType: {value.Type}");
        }
    }

    /// <summary>
    /// 将 CreateSeries 载荷写入 <see cref="SpanWriter"/>。
    /// </summary>
    internal static void WriteCreateSeriesPayload(
        ref SpanWriter writer,
        ulong seriesId,
        string measurement,
        IReadOnlyDictionary<string, string> tags)
    {
        writer.WriteUInt64(seriesId);

        int measBytes = _utf8.GetByteCount(measurement);
        writer.WriteInt32(measBytes);
        int written = _utf8.GetBytes(measurement, writer.FreeSpan);
        writer.Advance(written);

        writer.WriteInt32(tags.Count);
        foreach (var kv in tags)
        {
            int keyBytes = _utf8.GetByteCount(kv.Key);
            writer.WriteInt32(keyBytes);
            int keyWritten = _utf8.GetBytes(kv.Key, writer.FreeSpan);
            writer.Advance(keyWritten);

            int valBytes = _utf8.GetByteCount(kv.Value);
            writer.WriteInt32(valBytes);
            int valWritten = _utf8.GetBytes(kv.Value, writer.FreeSpan);
            writer.Advance(valWritten);
        }
    }

    /// <summary>
    /// 将 Checkpoint 载荷写入 <see cref="SpanWriter"/>。
    /// </summary>
    internal static void WriteCheckpointPayload(ref SpanWriter writer, long checkpointLsn)
        => writer.WriteInt64(checkpointLsn);

    /// <summary>
    /// 计算 Delete 载荷的字节大小。
    /// </summary>
    internal static int MeasureDelete(string fieldName)
    {
        int fieldNameBytes = _utf8.GetByteCount(fieldName);
        // SeriesId(8) + FromTimestamp(8) + ToTimestamp(8) + FieldNameLen(2) + FieldNameBytes
        return 8 + 8 + 8 + 2 + fieldNameBytes;
    }

    /// <summary>
    /// 将 Delete 载荷写入 <see cref="SpanWriter"/>。
    /// </summary>
    internal static void WriteDeletePayload(
        ref SpanWriter writer,
        ulong seriesId,
        string fieldName,
        long fromTimestamp,
        long toTimestamp)
    {
        writer.WriteUInt64(seriesId);
        writer.WriteInt64(fromTimestamp);
        writer.WriteInt64(toTimestamp);
        int fieldNameBytes = _utf8.GetByteCount(fieldName);
        writer.WriteUInt16((ushort)fieldNameBytes);
        int written = _utf8.GetBytes(fieldName, writer.FreeSpan);
        writer.Advance(written);
    }

    // ─────────────────────────── 读取 ──────────────────────────────────────

    /// <summary>
    /// 从 <see cref="SpanReader"/> 读取 WritePoint 载荷。
    /// </summary>
    internal static WritePointRecord ReadWritePointPayload(
        SpanReader reader,
        long lsn,
        long timestampUtcTicks)
    {
        ulong seriesId = reader.ReadUInt64();
        long pointTimestamp = reader.ReadInt64();
        var fieldType = (FieldType)reader.ReadByte();
        reader.Skip(3); // padding

        int fieldNameLen = reader.ReadInt32();
        string fieldName = _utf8.GetString(reader.ReadBytes(fieldNameLen));

        int valueLen = reader.ReadInt32();
        FieldValue value = fieldType switch
        {
            FieldType.Float64 => FieldValue.FromDouble(reader.ReadDouble()),
            FieldType.Int64 => FieldValue.FromLong(reader.ReadInt64()),
            FieldType.Boolean => FieldValue.FromBool(reader.ReadByte() != 0),
            FieldType.String => FieldValue.FromString(_utf8.GetString(reader.ReadBytes(valueLen))),
            _ => throw new InvalidDataException($"Unsupported FieldType in WAL: {fieldType}"),
        };

        return new WritePointRecord(lsn, timestampUtcTicks, seriesId, pointTimestamp, fieldName, value);
    }

    /// <summary>
    /// 从 <see cref="SpanReader"/> 读取 CreateSeries 载荷。
    /// </summary>
    internal static CreateSeriesRecord ReadCreateSeriesPayload(
        SpanReader reader,
        long lsn,
        long timestampUtcTicks)
    {
        ulong seriesId = reader.ReadUInt64();

        int measLen = reader.ReadInt32();
        string measurement = _utf8.GetString(reader.ReadBytes(measLen));

        int tagCount = reader.ReadInt32();
        var tags = new Dictionary<string, string>(tagCount, StringComparer.Ordinal);
        for (int i = 0; i < tagCount; i++)
        {
            int keyLen = reader.ReadInt32();
            string key = _utf8.GetString(reader.ReadBytes(keyLen));
            int valLen = reader.ReadInt32();
            string val = _utf8.GetString(reader.ReadBytes(valLen));
            tags[key] = val;
        }

        return new CreateSeriesRecord(lsn, timestampUtcTicks, seriesId, measurement, tags);
    }

    /// <summary>
    /// 从 <see cref="SpanReader"/> 读取 Checkpoint 载荷。
    /// </summary>
    internal static CheckpointRecord ReadCheckpointPayload(
        SpanReader reader,
        long lsn,
        long timestampUtcTicks)
    {
        long checkpointLsn = reader.ReadInt64();
        return new CheckpointRecord(lsn, timestampUtcTicks, checkpointLsn);
    }

    /// <summary>
    /// 从 <see cref="SpanReader"/> 读取 Delete 载荷。
    /// </summary>
    internal static DeleteRecord ReadDeletePayload(
        SpanReader reader,
        long lsn,
        long timestampUtcTicks)
    {
        ulong seriesId = reader.ReadUInt64();
        long fromTimestamp = reader.ReadInt64();
        long toTimestamp = reader.ReadInt64();
        int fieldNameLen = reader.ReadUInt16();
        string fieldName = _utf8.GetString(reader.ReadBytes(fieldNameLen));
        return new DeleteRecord(lsn, timestampUtcTicks, seriesId, fieldName, fromTimestamp, toTimestamp);
    }
}
