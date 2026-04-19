using System.Diagnostics;
using TSLite.Catalog;

namespace TSLite.Wal;

/// <summary>
/// WAL 回放辅助类，将 WAL 记录应用到 <see cref="SeriesCatalog"/> 并 yield 出写入点流。
/// </summary>
public static class WalReplay
{
    /// <summary>
    /// 对 catalog 应用 CreateSeries 记录，并返回回放出的 WritePoint 序列。
    /// </summary>
    /// <param name="walPath">WAL 文件路径。</param>
    /// <param name="catalog">目标序列目录，CreateSeries 记录将被应用到此 catalog。</param>
    /// <returns>回放出的 <see cref="WritePointRecord"/> 序列（按 LSN 顺序）。</returns>
    /// <exception cref="ArgumentNullException">任何参数为 null 时抛出。</exception>
    /// <exception cref="InvalidDataException">
    /// CreateSeries 记录的 SeriesId 与 catalog 重新计算的结果不一致时抛出。
    /// </exception>
    public static IEnumerable<WritePointRecord> ReplayInto(string walPath, SeriesCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(walPath);
        ArgumentNullException.ThrowIfNull(catalog);

        using var reader = WalReader.Open(walPath);
        foreach (var record in reader.Replay())
        {
            switch (record)
            {
                case CreateSeriesRecord csRecord:
                    var entry = catalog.GetOrAdd(csRecord.Measurement, csRecord.Tags);
                    if (entry.Id != csRecord.SeriesId)
                        throw new InvalidDataException(
                            $"WAL CreateSeries SeriesId mismatch for '{csRecord.Measurement}': " +
                            $"WAL={csRecord.SeriesId}, computed={entry.Id}.");
                    break;

                case WritePointRecord wpRecord:
                    yield return wpRecord;
                    break;

                case CheckpointRecord cpRecord:
                    Trace.WriteLine($"[WalReplay] Checkpoint LSN={cpRecord.CheckpointLsn}");
                    break;

                case TruncateRecord trRecord:
                    Trace.WriteLine($"[WalReplay] Truncate LSN={trRecord.Lsn}");
                    break;
            }
        }
    }
}
