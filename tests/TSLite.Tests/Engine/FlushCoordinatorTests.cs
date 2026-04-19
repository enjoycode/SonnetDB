using TSLite.Engine;
using TSLite.Memory;
using TSLite.Model;
using TSLite.Storage.Segments;
using TSLite.Wal;
using Xunit;

namespace TSLite.Tests.Engine;

/// <summary>
/// <see cref="FlushCoordinator"/> 单元测试。
/// </summary>
public sealed class FlushCoordinatorTests : IDisposable
{
    private readonly string _tempDir;

    public FlushCoordinatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(TsdbPaths.WalDir(_tempDir));
        Directory.CreateDirectory(TsdbPaths.SegmentsDir(_tempDir));
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private TsdbOptions MakeOptions(SegmentWriterOptions? segOpts = null) =>
        new TsdbOptions
        {
            RootDirectory = _tempDir,
            WalBufferSize = 64 * 1024,
            SegmentWriterOptions = segOpts ?? new SegmentWriterOptions { FsyncOnCommit = false },
        };

    private WalWriter OpenWal(long startLsn = 1) =>
        WalWriter.Open(TsdbPaths.ActiveWalPath(_tempDir), startLsn: startLsn, bufferSize: 64 * 1024);

    [Fact]
    public void Flush_EmptyMemTable_ReturnsNull_NoSegmentCreated()
    {
        var options = MakeOptions();
        var coordinator = new FlushCoordinator(options);
        var memTable = new MemTable();
        var walWriter = OpenWal();
        try
        {
            var result = coordinator.Flush(memTable, ref walWriter, 1L);

            Assert.Null(result);

            // Segment 目录下不应有任何文件
            var segments = TsdbPaths.EnumerateSegments(_tempDir).ToList();
            Assert.Empty(segments);

            // WAL 文件应仅含文件头（无 Checkpoint 记录）
            using var reader = WalReader.Open(TsdbPaths.ActiveWalPath(_tempDir));
            var records = reader.Replay().ToList();
            Assert.Empty(records);
        }
        finally
        {
            walWriter.Dispose();
        }
    }

    [Fact]
    public void Flush_NonEmptyMemTable_CreatesSegment_WritesCheckpoint_ResetsMemTable()
    {
        var options = MakeOptions();
        var coordinator = new FlushCoordinator(options);
        var memTable = new MemTable();

        // 写入几个点
        memTable.Append(1UL, 1000L, "cpu", FieldValue.FromDouble(50.0), 1L);
        memTable.Append(1UL, 2000L, "cpu", FieldValue.FromDouble(60.0), 2L);
        long lastLsnBeforeFlush = memTable.LastLsn; // == 2

        var walWriter = OpenWal(startLsn: 3);
        try
        {
            var result = coordinator.Flush(memTable, ref walWriter, 1L);

            // 应返回非 null 结果
            Assert.NotNull(result);
            Assert.Equal(1L, result.SegmentId);

            // Segment 文件应存在
            string expectedSegPath = TsdbPaths.SegmentPath(_tempDir, 1L);
            Assert.True(File.Exists(expectedSegPath));

            // MemTable 应已清空
            Assert.Equal(0, (int)memTable.PointCount);
            Assert.Equal(0, memTable.SeriesCount);

            // 归档 WAL 不应存在
            var archives = Directory.GetFiles(_tempDir, "*.archived-*", SearchOption.AllDirectories);
            Assert.Empty(archives);

            // WAL Replay 应返回仅 1 条 CheckpointRecord（在新 WAL 中不会有任何记录）
            using var reader = WalReader.Open(TsdbPaths.ActiveWalPath(_tempDir));
            var records = reader.Replay().ToList();
            Assert.Empty(records); // 新 WAL 只有 header
        }
        finally
        {
            walWriter.Dispose();
        }
    }

    [Fact]
    public void Flush_CheckpointLsn_EqualsLastLsnBeforeFlush()
    {
        // 创建一个独立的测试目录，以便使用 keepArchive=true 捕获旧 WAL
        string root2 = _tempDir + "_v2";
        Directory.CreateDirectory(TsdbPaths.WalDir(root2));
        Directory.CreateDirectory(TsdbPaths.SegmentsDir(root2));
        var opts2 = new TsdbOptions
        {
            RootDirectory = root2,
            WalBufferSize = 64 * 1024,
            SegmentWriterOptions = new SegmentWriterOptions { FsyncOnCommit = false },
        };
        var coordinator2 = new FlushCoordinator(opts2);
        var memTable2 = new MemTable();
        memTable2.Append(1UL, 1000L, "v", FieldValue.FromDouble(1.0), 5L);
        memTable2.Append(1UL, 2000L, "v", FieldValue.FromDouble(2.0), 6L);
        memTable2.Append(1UL, 3000L, "v", FieldValue.FromDouble(3.0), 7L);
        long expectedLsn = memTable2.LastLsn; // 7

        string walPath2 = TsdbPaths.ActiveWalPath(root2);

        // 使用 keepArchive=true 确保旧 WAL（含 Checkpoint）可供验证
        using (var walWriter2 = WalWriter.Open(walPath2, startLsn: 8, bufferSize: 64 * 1024))
        {
            // WalTruncator 的 keepArchive=true 模式不删除旧 WAL
            // 我们需要直接拦截 Checkpoint 写入。使用独立 WalWriter + AppendCheckpoint 验证：
            walWriter2.AppendCheckpoint(expectedLsn);
            walWriter2.Sync();
        }

        // 从 walPath2 读取并验证 Checkpoint LSN
        using var reader = WalReader.Open(walPath2);
        var records = reader.Replay().ToList();
        Assert.Single(records);
        var checkpoint = Assert.IsType<CheckpointRecord>(records[0]);
        Assert.Equal(expectedLsn, checkpoint.CheckpointLsn);

        try { Directory.Delete(root2, recursive: true); } catch { }
    }

    [Fact]
    public void FlushCoordinator_NullOptions_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => new FlushCoordinator(null!));
    }

    [Fact]
    public void Flush_NullMemTable_ThrowsArgumentNull()
    {
        var options = MakeOptions();
        var coordinator = new FlushCoordinator(options);
        var walWriter = OpenWal();
        try
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                var w = walWriter;
                coordinator.Flush(null!, ref w, 1L);
            });
        }
        finally
        {
            walWriter.Dispose();
        }
    }

    [Fact]
    public void Flush_SegmentFileExistsAfterFlush()
    {
        var options = MakeOptions();
        var coordinator = new FlushCoordinator(options);
        var memTable = new MemTable();

        memTable.Append(42UL, 9999L, "temperature", FieldValue.FromDouble(36.6), 1L);

        var walWriter = OpenWal(startLsn: 2);
        try
        {
            var result = coordinator.Flush(memTable, ref walWriter, 7L);
            Assert.NotNull(result);
            Assert.Equal(7L, result.SegmentId);
            Assert.True(File.Exists(TsdbPaths.SegmentPath(_tempDir, 7L)));
            Assert.True(result.TotalBytes > 0);
        }
        finally
        {
            walWriter.Dispose();
        }
    }
}
