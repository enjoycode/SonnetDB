using BenchmarkDotNet.Attributes;
using TSLite.Engine;
using TSLite.Engine.Compaction;
using TSLite.Engine.Retention;
using TSLite.Memory;
using TSLite.Model;
using TSLite.Storage.Segments;
using TSLite.Benchmarks.Helpers;

namespace TSLite.Benchmarks.Benchmarks;

/// <summary>
/// TSLite Compaction 基准：
/// 预先构造多个可合并段（同 tier），然后测量一次 SegmentCompactor.Execute 的耗时与输出大小。
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory("Compaction")]
public class CompactionBenchmark
{
    private const int SegmentCount = 8;
    private const int PointsPerSegment = 50_000;

    private readonly IReadOnlyDictionary<string, string> _tags = new Dictionary<string, string>
    {
        ["host"] = "server001",
    };

    private string _rootDirectory = string.Empty;
    private SegmentCompactor _compactor = null!;
    private CompactionPlan _plan = null!;
    private Dictionary<long, SegmentReader> _readersById = null!;
    private long _nextSegmentId;
    private string? _lastOutputPath;

    /// <summary>
    /// 全局初始化：创建临时库目录并写出多个段，准备一个可执行的 compaction plan。
    /// </summary>
    [GlobalSetup]
    public void GlobalSetup()
    {
        _rootDirectory = Path.Combine(Path.GetTempPath(), $"tslite_bench_compaction_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_rootDirectory);

        var options = new TsdbOptions
        {
            RootDirectory = _rootDirectory,
            FlushPolicy = new MemTableFlushPolicy
            {
                MaxBytes = long.MaxValue,
                MaxPoints = long.MaxValue,
                MaxAge = TimeSpan.MaxValue,
            },
            BackgroundFlush = new BackgroundFlushOptions { Enabled = false },
            Compaction = new CompactionPolicy { Enabled = false },
            Retention = new RetentionPolicy { Enabled = false },
        };

        using (var db = Tsdb.Open(options))
        {
            var allPoints = DataGenerator.Generate(SegmentCount * PointsPerSegment);
            int offset = 0;

            for (int segmentIndex = 0; segmentIndex < SegmentCount; segmentIndex++)
            {
                for (int i = 0; i < PointsPerSegment; i++)
                {
                    var dp = allPoints[offset++];
                    var point = Point.Create(
                        measurement: "sensor_data",
                        timestampUnixMs: dp.Timestamp,
                        tags: _tags,
                        fields: new Dictionary<string, FieldValue>
                        {
                            ["value"] = FieldValue.FromDouble(dp.Value),
                        });
                    db.Write(point);
                }

                db.FlushNow();
            }

            var segments = db.ListSegments();
            _readersById = new Dictionary<long, SegmentReader>(segments.Count);
            foreach (var (segmentId, path) in segments)
            {
                _readersById[segmentId] = SegmentReader.Open(path);
            }

            var sourceIds = _readersById.Keys.OrderBy(static id => id).Take(4).ToArray();
            if (sourceIds.Length < 4)
            {
                throw new InvalidOperationException("Compaction 基准初始化失败：可用段数量不足 4。");
            }

            _plan = new CompactionPlan(0, sourceIds);
            _nextSegmentId = _readersById.Keys.Max() + 1;
            _compactor = new SegmentCompactor(SegmentWriterOptions.Default);
        }
    }

    /// <summary>
    /// 测量一次 compaction 执行。
    /// </summary>
    /// <returns>输出段字节数，用于防止 BenchmarkDotNet 消除调用。</returns>
    [Benchmark(Description = "TSLite Compaction (4->1)")]
    public long TSLite_Compaction_4To1()
    {
        var segmentId = _nextSegmentId++;
        var outputPath = TsdbPaths.SegmentPath(_rootDirectory, segmentId);
        _lastOutputPath = outputPath;

        var result = _compactor.Execute(
            _plan,
            _readersById,
            segmentId,
            outputPath);

        return result.OutputBytes;
    }

    /// <summary>
    /// 每轮迭代后删除该轮输出段，避免磁盘累积影响后续测量。
    /// </summary>
    [IterationCleanup]
    public void IterationCleanup()
    {
        if (_lastOutputPath is not null && File.Exists(_lastOutputPath))
        {
            File.Delete(_lastOutputPath);
        }

        _lastOutputPath = null;
    }

    /// <summary>
    /// 释放 reader 并清理临时目录。
    /// </summary>
    [GlobalCleanup]
    public void GlobalCleanup()
    {
        if (_readersById is not null)
        {
            foreach (var reader in _readersById.Values)
            {
                reader.Dispose();
            }
        }

        if (!string.IsNullOrWhiteSpace(_rootDirectory) && Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }
}
