using System.Runtime.CompilerServices;
using TSLite.Buffers;
using TSLite.IO;
using TSLite.Storage.Format;
using Xunit;

namespace TSLite.Tests.Storage.Format;

/// <summary>
/// <see cref="BlockHeader"/> 单元测试。
/// </summary>
public sealed class BlockHeaderTests
{
    // ── Size ────────────────────────────────────────────────────────────────

    [Fact]
    public void Size_Is64Bytes()
        => Assert.Equal(FormatSizes.BlockHeaderSize, Unsafe.SizeOf<BlockHeader>());

    // ── Default ─────────────────────────────────────────────────────────────

    [Fact]
    public void Default_AllFieldsAreZero()
    {
        BlockHeader h = default;
        Assert.Equal(0UL, h.SeriesId);
        Assert.Equal(0L, h.MinTimestamp);
        Assert.Equal(0L, h.MaxTimestamp);
        Assert.Equal(0, h.Count);
        Assert.Equal(0, h.TimestampPayloadLength);
        Assert.Equal(0, h.ValuePayloadLength);
        Assert.Equal(0, h.FieldNameUtf8Length);
        Assert.Equal(BlockEncoding.None, h.Encoding);
        Assert.Equal(FieldType.Unknown, h.FieldType);
    }

    // ── CreateNew ───────────────────────────────────────────────────────────

    [Fact]
    public void CreateNew_SetsExpectedFields()
    {
        BlockHeader h = BlockHeader.CreateNew(
            seriesId: 123UL,
            min: 1000L,
            max: 2000L,
            count: 10,
            fieldType: FieldType.Float64,
            fieldNameLen: 5,
            tsLen: 80,
            valLen: 80);

        Assert.Equal(123UL, h.SeriesId);
        Assert.Equal(1000L, h.MinTimestamp);
        Assert.Equal(2000L, h.MaxTimestamp);
        Assert.Equal(10, h.Count);
        Assert.Equal(FieldType.Float64, h.FieldType);
        Assert.Equal(5, h.FieldNameUtf8Length);
        Assert.Equal(80, h.TimestampPayloadLength);
        Assert.Equal(80, h.ValuePayloadLength);
        Assert.Equal(BlockEncoding.None, h.Encoding);
    }

    // ── Round-trip ──────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_WriteStruct_ReadStruct_AllFieldsEqual()
    {
        BlockHeader original = BlockHeader.CreateNew(
            seriesId: 0xDEADBEEFUL,
            min: -100L,
            max: 100L,
            count: 50,
            fieldType: FieldType.Int64,
            fieldNameLen: 8,
            tsLen: 400,
            valLen: 400);
        original.Encoding = BlockEncoding.DeltaTimestamp;

        Span<byte> buffer = stackalloc byte[FormatSizes.BlockHeaderSize];
        var writer = new SpanWriter(buffer);
        writer.WriteStruct(in original);
        Assert.Equal(FormatSizes.BlockHeaderSize, writer.Position);

        var reader = new SpanReader(buffer);
        BlockHeader read = reader.ReadStruct<BlockHeader>();

        Assert.Equal(original.SeriesId, read.SeriesId);
        Assert.Equal(original.MinTimestamp, read.MinTimestamp);
        Assert.Equal(original.MaxTimestamp, read.MaxTimestamp);
        Assert.Equal(original.Count, read.Count);
        Assert.Equal(original.TimestampPayloadLength, read.TimestampPayloadLength);
        Assert.Equal(original.ValuePayloadLength, read.ValuePayloadLength);
        Assert.Equal(original.FieldNameUtf8Length, read.FieldNameUtf8Length);
        Assert.Equal(original.Encoding, read.Encoding);
        Assert.Equal(original.FieldType, read.FieldType);
    }

    // ── InlineBytes ─────────────────────────────────────────────────────────

    [Fact]
    public void InlineBytes_Reserved16_WriteAndRead_RoundTrip()
    {
        BlockHeader original = BlockHeader.CreateNew(1UL, 0L, 0L, 0, FieldType.Unknown, 0, 0, 0);
        original.Reserved16.AsSpan().Fill(0x77);

        Span<byte> buffer = stackalloc byte[FormatSizes.BlockHeaderSize];
        var writer = new SpanWriter(buffer);
        writer.WriteStruct(in original);

        var reader = new SpanReader(buffer);
        BlockHeader read = reader.ReadStruct<BlockHeader>();

        Assert.True(original.Reserved16.AsReadOnlySpan().SequenceEqual(read.Reserved16.AsReadOnlySpan()));
    }
}
