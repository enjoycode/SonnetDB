using TSLite.Model;
using TSLite.Storage.Format;
using TSLite.Wal;
using Xunit;

namespace TSLite.Tests.Wal;

/// <summary>
/// <see cref="WalReader"/> 单元测试。
/// </summary>
public sealed class WalReaderTests : IDisposable
{
    private readonly string _tempDir;

    public WalReaderTests()
    {
        _tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
        System.IO.Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { System.IO.Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string TempFile() => System.IO.Path.Combine(_tempDir, System.IO.Path.GetRandomFileName() + ".tslwal");

    private static void WriteMixedRecords(string path, int count)
    {
        using var writer = WalWriter.Open(path);
        var tags = new Dictionary<string, string> { ["host"] = "srv1" };
        writer.AppendCreateSeries(1UL, "cpu", tags);
        for (int i = 0; i < count - 1; i++)
            writer.AppendWritePoint(1UL, 1000L + i, "usage", FieldValue.FromDouble(i * 0.1));
        writer.Sync();
    }

    [Fact]
    public void RoundTrip_100MixedRecords_AllRead()
    {
        string path = TempFile();
        WriteMixedRecords(path, 100);

        var records = new List<WalRecord>();
        using var reader = WalReader.Open(path);
        records.AddRange(reader.Replay());

        Assert.Equal(100, records.Count);
        Assert.IsType<CreateSeriesRecord>(records[0]);
        for (int i = 1; i < 100; i++)
            Assert.IsType<WritePointRecord>(records[i]);
    }

    [Fact]
    public void TruncatedFile_StopsWithoutThrow()
    {
        string path = TempFile();
        WriteMixedRecords(path, 10);

        // Truncate last 5 bytes
        var fi = new System.IO.FileInfo(path);
        using (var fs = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite))
            fs.SetLength(fi.Length - 5);

        var records = new List<WalRecord>();
        Exception? ex = null;
        try
        {
            using var reader = WalReader.Open(path);
            records.AddRange(reader.Replay());
        }
        catch (Exception e) { ex = e; }

        Assert.Null(ex);
        Assert.True(records.Count <= 9); // at most 9 complete records
    }

    [Fact]
    public void CorruptedLastPayload_CrcFailure_StopsWithoutThrow()
    {
        string path = TempFile();
        WriteMixedRecords(path, 10);

        // Corrupt the last byte of the file (payload of last record)
        using (var fs = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite))
        {
            fs.Position = fs.Length - 1;
            int b = fs.ReadByte();
            fs.Position = fs.Length - 1;
            fs.WriteByte((byte)(b ^ 0xFF));
        }

        var records = new List<WalRecord>();
        Exception? ex = null;
        try
        {
            using var reader = WalReader.Open(path);
            records.AddRange(reader.Replay());
        }
        catch (Exception e) { ex = e; }

        Assert.Null(ex);
        Assert.True(records.Count <= 9);
    }

    [Fact]
    public void InvalidMagicHeader_ThrowsInvalidDataException()
    {
        string path = TempFile();
        WriteMixedRecords(path, 5);

        // Corrupt the file header magic
        using (var fs = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite))
        {
            fs.Position = 0;
            fs.WriteByte(0xFF);
        }

        Assert.Throws<InvalidDataException>(() => WalReader.Open(path));
    }

    [Fact]
    public void LastValidOffset_IsCorrectAfterReplay()
    {
        string path = TempFile();
        WriteMixedRecords(path, 5);

        using var reader = WalReader.Open(path);
        var records = reader.Replay().ToList();

        Assert.Equal(5, records.Count);
        // LastValidOffset should be at the end of the 5th record
        Assert.Equal(reader.BytesRead, reader.LastValidOffset);
        Assert.True(reader.LastValidOffset > FormatSizes.WalFileHeaderSize);
    }
}
