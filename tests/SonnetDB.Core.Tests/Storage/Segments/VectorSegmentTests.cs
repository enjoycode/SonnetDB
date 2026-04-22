using System.Buffers.Binary;
using System.Runtime.InteropServices;
using SonnetDB.Buffers;
using SonnetDB.Catalog;
using SonnetDB.Engine;
using SonnetDB.Memory;
using SonnetDB.Model;
using SonnetDB.Storage.Format;
using SonnetDB.Storage.Segments;
using Xunit;

namespace SonnetDB.Core.Tests.Storage.Segments;

/// <summary>
/// PR #58 c：<see cref="BlockEncoding.VectorRaw"/> + Segment v3 单元测试。
/// 覆盖：Raw 编解码 round-trip、SegmentWriter→SegmentReader 端到端、维度不一致的写入校验、
/// 段格式版本号升级到 v3 及对 v2 的只读兼容入口。
/// </summary>
public sealed class VectorSegmentTests : IDisposable
{
    private readonly string _tempDir;

    public VectorSegmentTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ── ValuePayloadCodec：VectorRaw 编解码 ─────────────────────────────────

    [Fact]
    public void Vector_Measure_EqualsCountTimesDimTimesFour()
    {
        var points = MakeVectorPoints(dim: 4, count: 3, seed: 1);
        int measured = ValuePayloadCodec.MeasureValuePayload(FieldType.Vector, points);
        Assert.Equal(3 * 4 * sizeof(float), measured);
    }

    [Fact]
    public void Vector_WritePayload_ProducesLittleEndianFloat32Sequence()
    {
        float[][] vectors = [[1f, 2f, 3f], [4f, 5f, 6f]];
        var points = MakeVectorPoints(vectors);

        int len = ValuePayloadCodec.MeasureValuePayload(FieldType.Vector, points);
        byte[] buf = new byte[len];
        ValuePayloadCodec.WritePayload(FieldType.Vector, points, buf);

        Assert.Equal(2 * 3 * sizeof(float), len);
        for (int p = 0; p < 2; p++)
        {
            for (int i = 0; i < 3; i++)
            {
                int offset = (p * 3 + i) * sizeof(float);
                int rawBits = BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(offset, 4));
                float read = BitConverter.Int32BitsToSingle(rawBits);
                Assert.Equal(vectors[p][i], read);
            }
        }
    }

    [Fact]
    public void Vector_WritePayload_DimensionMismatch_Throws()
    {
        float[][] vectors = [[1f, 2f, 3f], [4f, 5f]]; // 第二个 dim=2，与首个 dim=3 不一致
        var points = MakeVectorPoints(vectors);
        Assert.Throws<InvalidOperationException>(() =>
            ValuePayloadCodec.MeasureValuePayload(FieldType.Vector, points));
    }

    // ── SegmentWriter / SegmentReader：端到端 round-trip ───────────────────

    [Fact]
    public void Segment_WriteAndRead_VectorBlock_RoundTripPreservesAllValues()
    {
        const int dim = 4;
        const int count = 5;
        ulong seriesId = 9001UL;
        const string fieldName = "embedding";

        var mt = new MemTable();
        var expected = new List<DataPoint>(count);
        long lsn = 1L;
        for (int i = 0; i < count; i++)
        {
            float[] vec = new float[dim];
            for (int d = 0; d < dim; d++)
                vec[d] = i * 10f + d * 0.5f;
            long ts = 1_000L + i * 10L;
            mt.Append(seriesId, ts, fieldName, FieldValue.FromVector(vec), lsn++);
            expected.Add(new DataPoint(ts, FieldValue.FromVector(vec)));
        }

        string path = Path.Combine(_tempDir, "vector.SDBSEG");
        var writer = new SegmentWriter(new SegmentWriterOptions { FsyncOnCommit = false });
        writer.WriteFrom(mt, segmentId: 42L, path);

        using var reader = SegmentReader.Open(path);
        Assert.Equal(1, reader.BlockCount);

        var blocks = reader.FindBySeries(seriesId);
        Assert.Single(blocks);
        var block = blocks[0];

        Assert.Equal(FieldType.Vector, block.FieldType);
        Assert.Equal(BlockEncoding.VectorRaw, block.ValueEncoding);
        Assert.Equal(count, block.Count);
        Assert.Equal(count * dim * sizeof(float), block.ValuePayloadLength);

        DataPoint[] decoded = reader.DecodeBlock(block);
        Assert.Equal(count, decoded.Length);
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(expected[i].Timestamp, decoded[i].Timestamp);
            Assert.True(expected[i].Value.AsVector().Span.SequenceEqual(decoded[i].Value.AsVector().Span));
        }
    }

    [Fact]
    public void Segment_WriteWithHnswVectorIndex_CreatesSidecarAndReaderLoadsIt()
    {
        ulong seriesId = 4242UL;
        const string fieldName = "embedding";

        var mt = new MemTable();
        long lsn = 1L;
        for (int i = 0; i < 8; i++)
            mt.Append(seriesId, 1_000L + i, fieldName, FieldValue.FromVector(new[] { (float)i, 0f, 0f }), lsn++);

        string path = Path.Combine(_tempDir, "vector-index.SDBSEG");
        var vectorIndexes = new Dictionary<SeriesFieldKey, VectorIndexDefinition>
        {
            [new SeriesFieldKey(seriesId, fieldName)] = VectorIndexDefinition.CreateHnsw(4, 8),
        };

        var writer = new SegmentWriter(new SegmentWriterOptions { FsyncOnCommit = false });
        writer.WriteFrom(mt, segmentId: 7L, path, vectorIndexes);

        string sidecarPath = TsdbPaths.VectorIndexPathForSegment(path);
        Assert.True(File.Exists(sidecarPath));

        using var reader = SegmentReader.Open(path);
        var block = Assert.Single(reader.FindBySeries(seriesId));
        Assert.True(reader.TryGetVectorIndex(block, out var vectorIndex));
        Assert.Equal(block.Index, vectorIndex.BlockIndex);
        Assert.Equal(8, vectorIndex.Count);
        Assert.Equal(3, vectorIndex.Dimension);
    }

    // ── Segment 文件头：版本号升级到 v3 + v2 只读兼容 ──────────────────────

    [Fact]
    public void TsdbMagic_SegmentFormatVersion_IsThree()
        => Assert.Equal(3, TsdbMagic.SegmentFormatVersion);

    [Fact]
    public void TsdbMagic_SupportedSegmentFormatVersions_ContainsV2AndV3()
    {
        int[] supported = TsdbMagic.SupportedSegmentFormatVersions.ToArray();
        Assert.Contains(2, supported);
        Assert.Contains(3, supported);
    }

    [Fact]
    public void SegmentHeader_IsCompatibleForRead_AcceptsV2AndV3_RejectsOthers()
    {
        var h = SegmentHeader.CreateNew(1L);

        h.FormatVersion = 3;
        Assert.True(h.IsCompatibleForRead());

        h.FormatVersion = 2;
        Assert.True(h.IsCompatibleForRead());

        h.FormatVersion = 1;
        Assert.False(h.IsCompatibleForRead());

        h.FormatVersion = 99;
        Assert.False(h.IsCompatibleForRead());
    }

    [Fact]
    public void SegmentReader_Open_AcceptsV2DowngradedSegment()
    {
        // 写一个普通 Float64 段，再把文件里的 v3 版本字段改回 v2，验证 SegmentReader 仍然可读。
        ulong seriesId = 7777UL;
        var mt = new MemTable();
        long lsn = 1L;
        for (int i = 0; i < 4; i++)
            mt.Append(seriesId, 1_000L + i, "v", FieldValue.FromDouble(i * 0.1), lsn++);

        string path = Path.Combine(_tempDir, "downgraded.SDBSEG");
        new SegmentWriter(new SegmentWriterOptions { FsyncOnCommit = false }).WriteFrom(mt, segmentId: 1L, path);

        // 把 SegmentHeader.FormatVersion 与 SegmentFooter.FormatVersion 由 3 改为 2。
        // FormatVersion 在两处均位于 magic(8B) 之后偏移 8 处，4 字节 LE int。
        byte[] bytes = File.ReadAllBytes(path);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8, 4), 2);
        // Footer 起始偏移 = bytes.Length - SegmentFooterSize
        int footerStart = bytes.Length - FormatSizes.SegmentFooterSize;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(footerStart + 8, 4), 2);
        File.WriteAllBytes(path, bytes);

        using var reader = SegmentReader.Open(path);
        Assert.Equal(1, reader.BlockCount);
        var blocks = reader.FindBySeries(seriesId);
        var decoded = reader.DecodeBlock(blocks[0]);
        Assert.Equal(4, decoded.Length);
    }

    // ── 辅助 ────────────────────────────────────────────────────────────────

    private static ReadOnlyMemory<DataPoint> MakeVectorPoints(int dim, int count, int seed)
    {
        var arr = new DataPoint[count];
        for (int i = 0; i < count; i++)
        {
            float[] vec = new float[dim];
            for (int d = 0; d < dim; d++)
                vec[d] = (seed + i) * 0.1f + d;
            arr[i] = new DataPoint(i * 1000L, FieldValue.FromVector(vec));
        }
        return arr;
    }

    private static ReadOnlyMemory<DataPoint> MakeVectorPoints(float[][] vectors)
    {
        var arr = new DataPoint[vectors.Length];
        for (int i = 0; i < vectors.Length; i++)
            arr[i] = new DataPoint(i * 1000L, FieldValue.FromVector(vectors[i]));
        return arr;
    }
}
