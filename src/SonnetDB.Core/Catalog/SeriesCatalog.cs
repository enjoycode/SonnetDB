using System.Collections.Concurrent;
using SonnetDB.Model;

namespace SonnetDB.Catalog;

/// <summary>
/// 序列目录：维护 SeriesKey ↔ SeriesId ↔ <see cref="SeriesEntry"/> 的双向映射。
/// 线程安全：基于 <see cref="ConcurrentDictionary{TKey,TValue}"/>，单写多读友好。
/// </summary>
/// <remarks>
/// <para>
/// 并发幂等性保证：<see cref="GetOrAdd(string,IReadOnlyDictionary{string,string})"/> 对同一
/// <see cref="SeriesKey"/> 的多次调用（包括并发调用）返回同一 <see cref="SeriesEntry"/> 实例。
/// 这依赖于 <see cref="ConcurrentDictionary{TKey,TValue}.GetOrAdd(TKey, TValue)"/> 的原子语义——
/// 对同一 key 的并发操作中，只有一个值会被存储，所有调用方拿到的都是这个"赢家"值。
/// </para>
/// </remarks>
public sealed class SeriesCatalog
{
    private readonly ConcurrentDictionary<string, SeriesEntry> _byCanonical
        = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<ulong, SeriesEntry> _byId = new();

    private readonly TagInvertedIndex _tagIndex = new();

    /// <summary>目录中的序列数量。</summary>
    public int Count => _byCanonical.Count;

    /// <summary>
    /// 取得或创建一条 series 目录项。同一 <see cref="SeriesKey"/> 重复调用幂等。
    /// </summary>
    /// <param name="measurement">Measurement 名称。</param>
    /// <param name="tags">Tag 键值对；为 null 时等同于空字典。</param>
    /// <returns>对应的 <see cref="SeriesEntry"/>（已存在则返回已有实例）。</returns>
    /// <exception cref="InvalidOperationException">检测到 SeriesId 哈希碰撞时抛出。</exception>
    public SeriesEntry GetOrAdd(string measurement, IReadOnlyDictionary<string, string>? tags)
    {
        var key = new SeriesKey(measurement, tags);
        return GetOrAddInternal(key);
    }

    /// <summary>
    /// 从 <see cref="Point"/> 直接派生，取得或创建对应的目录项。
    /// </summary>
    /// <param name="point">已校验的数据点。</param>
    /// <returns>对应的 <see cref="SeriesEntry"/>。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="point"/> 为 null。</exception>
    /// <exception cref="InvalidOperationException">检测到 SeriesId 哈希碰撞时抛出。</exception>
    public SeriesEntry GetOrAdd(Point point)
    {
        ArgumentNullException.ThrowIfNull(point);
        var key = SeriesKey.FromPoint(point);
        return GetOrAddInternal(key);
    }

    /// <summary>
    /// 按 SeriesId 查找；未命中返回 null。
    /// </summary>
    /// <param name="id">序列唯一标识（XxHash64 值）。</param>
    /// <returns>找到的 <see cref="SeriesEntry"/>，未命中返回 null。</returns>
    public SeriesEntry? TryGet(ulong id)
        => _byId.TryGetValue(id, out var entry) ? entry : null;

    /// <summary>
    /// 按 <see cref="SeriesKey"/> 查找；未命中返回 null。
    /// </summary>
    /// <param name="key">规范化序列键。</param>
    /// <returns>找到的 <see cref="SeriesEntry"/>，未命中返回 null。</returns>
    public SeriesEntry? TryGet(in SeriesKey key)
        => _byCanonical.TryGetValue(key.Canonical, out var entry) ? entry : null;

    /// <summary>
    /// 按 measurement 和部分 tag 过滤匹配的 series。
    /// 背后由 <see cref="TagInvertedIndex"/> 在 <c>(measurement, tagKey, tagValue)</c> 三级映射上
    /// 做候选交集，避免全表扫描；带 tag 过滤时复杂度 = 最小候选集大小 × 过滤条目数。
    /// 返回前依然在上层做防御性 tag 重校验，以容忍倒排索引与 _byCanonical 瞬间不一致。
    /// </summary>
    /// <param name="measurement">要筛选的 Measurement 名称。</param>
    /// <param name="tagFilter">Tag 子集过滤条件；为 null 或空时仅按 measurement 筛选。</param>
    /// <returns>匹配的 <see cref="SeriesEntry"/> 列表（快照）。</returns>
    public IReadOnlyList<SeriesEntry> Find(
        string measurement,
        IReadOnlyDictionary<string, string>? tagFilter)
    {
        ArgumentNullException.ThrowIfNull(measurement);

        var candidateIds = _tagIndex.Find(measurement, tagFilter);
        if (candidateIds.Count == 0)
            return [];

        var results = new List<SeriesEntry>(candidateIds.Count);
        foreach (var id in candidateIds)
        {
            if (!_byId.TryGetValue(id, out var entry))
                continue;
            // 防御性二次校验：measurement 与 tag 过滤全部命中才返回。
            if (!string.Equals(entry.Measurement, measurement, StringComparison.Ordinal))
                continue;
            if (tagFilter != null && tagFilter.Count > 0 && !MatchesTags(entry, tagFilter))
                continue;
            results.Add(entry);
        }
        return results;
    }

    private static bool MatchesTags(SeriesEntry entry, IReadOnlyDictionary<string, string> tagFilter)
    {
        foreach (var (k, v) in tagFilter)
        {
            if (!entry.Tags.TryGetValue(k, out var entryVal) ||
                !string.Equals(entryVal, v, StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    /// <summary>
    /// 枚举全部目录项（调用时拷贝的快照）。
    /// </summary>
    /// <returns>包含所有目录项的只读列表。</returns>
    public IReadOnlyList<SeriesEntry> Snapshot()
        => [.. _byCanonical.Values];

    /// <summary>
    /// 清空目录（仅供测试 / 重建用）。
    /// </summary>
    public void Clear()
    {
        _byCanonical.Clear();
        _byId.Clear();
        _tagIndex.Clear();
    }

    // ── 内部辅助 ──────────────────────────────────────────────────────────────

    private SeriesEntry GetOrAddInternal(SeriesKey key)
    {
        // 快速路径：已存在
        if (_byCanonical.TryGetValue(key.Canonical, out var existing))
            return existing;

        ulong id = SeriesId.Compute(key);
        long createdAt = DateTime.UtcNow.Ticks;
        var candidate = new SeriesEntry(id, key, key.Measurement, key.Tags, createdAt);

        // 原子操作：只有一个线程的 candidate 会胜出存入 _byCanonical
        var entry = _byCanonical.GetOrAdd(key.Canonical, candidate);

        // 确保 _byId 中持有胜出的条目（非 candidate 的调用直接传入同一 entry）
        var idEntry = _byId.GetOrAdd(id, entry);

        // 检测哈希碰撞：不同 canonical 映射到相同 id
        if (!ReferenceEquals(idEntry, entry) &&
            !string.Equals(idEntry.Key.Canonical, key.Canonical, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"SeriesId hash collision detected for series: {key.Canonical}");
        }
        // 仅在当前线程胜出（candidate 被存入）时才追加到倒排索引，避免重复填充；
        // TagInvertedIndex.Add 本身也是幂等的（块内动作为 set 幂等写入）。
        if (ReferenceEquals(entry, candidate))
            _tagIndex.Add(entry);
        return entry;
    }

    /// <summary>
    /// 从持久化加载时直接注入条目（跳过 GetOrAdd 路径以保留原始 CreatedAtUtcTicks）。
    /// 不是线程安全的，仅供 <see cref="CatalogFileCodec"/> 在构建新实例时使用。
    /// </summary>
    /// <param name="entry">要注入的目录项。</param>
    internal void LoadEntry(SeriesEntry entry)
    {
        _byCanonical.TryAdd(entry.Key.Canonical, entry);
        _byId.TryAdd(entry.Id, entry);
        _tagIndex.Add(entry);
    }
}
