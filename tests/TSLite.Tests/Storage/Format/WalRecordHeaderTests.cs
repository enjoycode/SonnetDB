using System.Runtime.CompilerServices;
using TSLite.IO;
using TSLite.Storage.Format;
using Xunit;

namespace TSLite.Tests.Storage.Format;

/// <summary>
/// <see cref="WalRecordHeader"/> 单元测试。
/// </summary>
public sealed class WalRecordHeaderTests
{
    // ── Size ────────────────────────────────────────────────────────────────

    [Fact]
    public void Size_Is32Bytes()
        => Assert.Equal(FormatSizes.WalRecordHeaderSize, Unsafe.SizeOf<WalRecordHeader>());

    // ── Default ─────────────────────────────────────────────────────────────

    [Fact]
    public void Default_AllFieldsAreZero()
    {
        WalRecordHeader h = default;
        Assert.Equal(WalRecordType.Unknown, h.RecordType);
        Assert.Equal(0, h.PayloadLength);
        Assert.Equal(0UL, h.SeriesId);
        Assert.Equal(0L, h.Timestamp);
        Assert.Equal(0U, h.Crc32);
    }

    // ── CreateNew ───────────────────────────────────────────────────────────

    [Fact]
    public void CreateNew_SetsExpectedFields()
    {
        WalRecordHeader h = WalRecordHeader.CreateNew(
            recordType: WalRecordType.Write,
            seriesId: 777UL,
            timestamp: 1_000_000L,
            payloadLength: 16);

        Assert.Equal(WalRecordType.Write, h.RecordType);
        Assert.Equal(777UL, h.SeriesId);
        Assert.Equal(1_000_000L, h.Timestamp);
        Assert.Equal(16, h.PayloadLength);
    }

    // ── Round-trip ──────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_WriteStruct_ReadStruct_AllFieldsEqual()
    {
        WalRecordHeader original = WalRecordHeader.CreateNew(
            recordType: WalRecordType.Checkpoint,
            seriesId: 0xFEEDFACEUL,
            timestamp: 9_999_999L,
            payloadLength: 0);
        original.Crc32 = 0x12345678U;

        Span<byte> buffer = stackalloc byte[FormatSizes.WalRecordHeaderSize];
        var writer = new SpanWriter(buffer);
        writer.WriteStruct(in original);
        Assert.Equal(FormatSizes.WalRecordHeaderSize, writer.Position);

        var reader = new SpanReader(buffer);
        WalRecordHeader read = reader.ReadStruct<WalRecordHeader>();

        Assert.Equal(original.RecordType, read.RecordType);
        Assert.Equal(original.PayloadLength, read.PayloadLength);
        Assert.Equal(original.SeriesId, read.SeriesId);
        Assert.Equal(original.Timestamp, read.Timestamp);
        Assert.Equal(original.Crc32, read.Crc32);
    }

    // ── Enum values ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(WalRecordType.Unknown, (byte)0)]
    [InlineData(WalRecordType.Write, (byte)1)]
    [InlineData(WalRecordType.Checkpoint, (byte)2)]
    [InlineData(WalRecordType.CatalogUpdate, (byte)3)]
    public void WalRecordType_ByteValues_AreCorrect(WalRecordType type, byte expected)
        => Assert.Equal(expected, (byte)type);

    [Fact]
    public void RoundTrip_AllRecordTypes_SerializeCorrectly()
    {
        Span<byte> buffer = stackalloc byte[FormatSizes.WalRecordHeaderSize];
        foreach (WalRecordType type in Enum.GetValues<WalRecordType>())
        {
            WalRecordHeader original = WalRecordHeader.CreateNew(type, 1UL, 1L, 0);
            buffer.Clear();
            var writer = new SpanWriter(buffer);
            writer.WriteStruct(in original);

            var reader = new SpanReader(buffer);
            WalRecordHeader read = reader.ReadStruct<WalRecordHeader>();
            Assert.Equal(type, read.RecordType);
        }
    }
}
