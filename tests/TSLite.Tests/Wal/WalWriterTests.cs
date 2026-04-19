using TSLite.Model;
using TSLite.Storage.Format;
using TSLite.Wal;
using Xunit;

namespace TSLite.Tests.Wal;

/// <summary>
/// <see cref="WalWriter"/> 单元测试。
/// </summary>
public sealed class WalWriterTests : IDisposable
{
    private readonly string _tempDir;

    public WalWriterTests()
    {
        _tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
        System.IO.Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { System.IO.Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string TempFile() => System.IO.Path.Combine(_tempDir, System.IO.Path.GetRandomFileName() + ".tslwal");

    [Fact]
    public void NewFile_HasFileHeader_ThenRecords()
    {
        string path = TempFile();
        using var writer = WalWriter.Open(path);

        writer.AppendWritePoint(1UL, 1000L, "temp", FieldValue.FromDouble(42.0));
        writer.AppendWritePoint(1UL, 2000L, "temp", FieldValue.FromDouble(43.0));
        writer.Sync();

        long fileSize = new System.IO.FileInfo(path).Length;
        Assert.True(fileSize >= FormatSizes.WalFileHeaderSize);
        Assert.Equal(writer.BytesWritten, fileSize);
    }

    [Fact]
    public void NextLsn_IsMonotonicallyIncreasing()
    {
        string path = TempFile();
        using var writer = WalWriter.Open(path);

        Assert.Equal(1L, writer.NextLsn);
        long lsn1 = writer.AppendWritePoint(1UL, 1000L, "f1", FieldValue.FromDouble(1.0));
        Assert.Equal(1L, lsn1);
        Assert.Equal(2L, writer.NextLsn);

        long lsn2 = writer.AppendWritePoint(1UL, 2000L, "f2", FieldValue.FromDouble(2.0));
        Assert.Equal(2L, lsn2);
        Assert.Equal(3L, writer.NextLsn);
    }

    [Fact]
    public void BytesWritten_MatchesExpectedSize()
    {
        string path = TempFile();
        using var writer = WalWriter.Open(path);

        Assert.Equal(FormatSizes.WalFileHeaderSize, writer.BytesWritten);

        writer.AppendCheckpoint(0L);
        int expectedPayloadSize = 8; // checkpoint payload = 8 bytes
        int expectedRecordSize = FormatSizes.WalRecordHeaderSize + expectedPayloadSize;
        Assert.Equal(FormatSizes.WalFileHeaderSize + expectedRecordSize, writer.BytesWritten);
    }

    [Fact]
    public void WriteAfterDispose_ThrowsObjectDisposedException()
    {
        string path = TempFile();
        var writer = WalWriter.Open(path);
        writer.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            writer.AppendWritePoint(1UL, 1000L, "f", FieldValue.FromDouble(1.0)));
    }

    [Fact]
    public void OpenExistingFile_ContinuesFromLastLsn()
    {
        string path = TempFile();

        // Write first batch
        using (var writer = WalWriter.Open(path, startLsn: 1))
        {
            writer.AppendWritePoint(1UL, 1000L, "temp", FieldValue.FromDouble(1.0));
            writer.AppendWritePoint(1UL, 2000L, "temp", FieldValue.FromDouble(2.0));
            writer.Sync();
        }

        // Reopen and continue
        using (var writer = WalWriter.Open(path))
        {
            Assert.Equal(3L, writer.NextLsn); // continues from 3
            long lsn = writer.AppendWritePoint(1UL, 3000L, "temp", FieldValue.FromDouble(3.0));
            Assert.Equal(3L, lsn);
        }
    }

    [Fact]
    public void SyncAndReopen_AllDataReadable()
    {
        string path = TempFile();
        using (var writer = WalWriter.Open(path))
        {
            writer.AppendWritePoint(1UL, 1000L, "temp", FieldValue.FromDouble(10.0));
            writer.AppendWritePoint(2UL, 2000L, "pressure", FieldValue.FromDouble(20.0));
            writer.Sync();
        }

        var records = new List<WalRecord>();
        using var reader = WalReader.Open(path);
        records.AddRange(reader.Replay());

        Assert.Equal(2, records.Count);
        var wp0 = Assert.IsType<WritePointRecord>(records[0]);
        Assert.Equal(1UL, wp0.SeriesId);
        Assert.Equal(1000L, wp0.PointTimestamp);
    }

    [Fact]
    public void IsOpen_TrueBeforeDispose_FalseAfter()
    {
        string path = TempFile();
        var writer = WalWriter.Open(path);
        Assert.True(writer.IsOpen);
        writer.Dispose();
        Assert.False(writer.IsOpen);
    }
}
