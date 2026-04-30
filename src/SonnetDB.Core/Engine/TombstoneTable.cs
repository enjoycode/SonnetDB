namespace SonnetDB.Engine;

/// <summary>
/// 进程内 Tombstone 集合：按 (SeriesId, FieldName) 索引，提供"某点是否被覆盖"的常数级判定。
/// <para>
/// 线程安全：lock 写 + Volatile 读快照。每次写操作后重建只读快照，读操作无锁。
/// </para>
/// </summary>
public sealed class TombstoneTable
{
    private readonly object _lock = new();
    private readonly Dictionary<(ulong SeriesId, string FieldName), List<Tombstone>> _byKey = new();
    private IReadOnlyList<Tombstone> _allSnapshot = [];

    /// <summary>当前墓碑总数。</summary>
    public int Count
    {
        get
        {
            lock (_lock)
                return _allSnapshot.Count;
        }
    }

    /// <summary>当前所有墓碑的只读快照（不可变，可无锁读取）。</summary>
    public IReadOnlyList<Tombstone> All => Volatile.Read(ref _allSnapshot);

    /// <summary>
    /// 追加一个墓碑。
    /// </summary>
    /// <param name="tombstone">要追加的墓碑。</param>
    public void Add(in Tombstone tombstone)
    {
        lock (_lock)
        {
            if (AddLocked(tombstone))
                RebuildSnapshot();
        }
    }

    /// <summary>
    /// 批量加载（启动时用）。已有的墓碑不会被清除，新加载的追加到集合中。
    /// </summary>
    /// <param name="tombstones">要加载的墓碑列表。</param>
    /// <exception cref="ArgumentNullException"><paramref name="tombstones"/> 为 null 时抛出。</exception>
    public void LoadFrom(IReadOnlyList<Tombstone> tombstones)
    {
        ArgumentNullException.ThrowIfNull(tombstones);

        if (tombstones.Count == 0)
            return;

        lock (_lock)
        {
            bool changed = false;
            foreach (var tomb in tombstones)
                changed |= AddLocked(tomb);

            if (changed)
                RebuildSnapshot();
        }
    }

    /// <summary>
    /// 判定一个点是否被任何墓碑覆盖。
    /// </summary>
    /// <param name="seriesId">序列唯一标识。</param>
    /// <param name="fieldName">字段名称。</param>
    /// <param name="timestamp">数据点时间戳（Unix 毫秒）。</param>
    /// <returns>若点被任何墓碑覆盖则返回 <c>true</c>，否则返回 <c>false</c>。</returns>
    public bool IsCovered(ulong seriesId, string fieldName, long timestamp)
    {
        var snapshot = Volatile.Read(ref _allSnapshot);
        if (snapshot.Count == 0)
            return false;

        // 锁内对桶列表做数组快照，避免锁外遍历时被写入方 List.Add 修改导致枚举异常
        Tombstone[] bucket;
        lock (_lock)
        {
            if (!_byKey.TryGetValue((seriesId, fieldName), out var list))
                return false;
            bucket = list.ToArray();
        }

        return IsCoveredByArray(timestamp, bucket);
    }

    /// <summary>
    /// 取某 (seriesId, fieldName) 的全部墓碑快照（用于查询时窗剪枝）。
    /// </summary>
    /// <param name="seriesId">序列唯一标识。</param>
    /// <param name="fieldName">字段名称。</param>
    /// <returns>该 (seriesId, fieldName) 对应的墓碑只读列表；若无则返回空列表。</returns>
    public IReadOnlyList<Tombstone> GetForSeriesField(ulong seriesId, string fieldName)
    {
        lock (_lock)
        {
            if (_byKey.TryGetValue((seriesId, fieldName), out var list))
                return list.ToArray();
        }
        return [];
    }

    /// <summary>
    /// 批量移除指定墓碑（仅供 Compaction 消化后调用）。
    /// </summary>
    /// <param name="tombstones">要移除的墓碑列表。</param>
    internal void RemoveAll(IReadOnlyList<Tombstone> tombstones)
    {
        if (tombstones.Count == 0)
            return;

        lock (_lock)
        {
            foreach (var tomb in tombstones)
            {
                var key = (tomb.SeriesId, tomb.FieldName);
                if (_byKey.TryGetValue(key, out var list))
                {
                    list.Remove(tomb);
                    if (list.Count == 0)
                        _byKey.Remove(key);
                }
            }
            RebuildSnapshot();
        }
    }

    // ── 私有辅助 ──────────────────────────────────────────────────────────────

    private bool AddLocked(in Tombstone tombstone)
    {
        var key = (tombstone.SeriesId, tombstone.FieldName);
        if (!_byKey.TryGetValue(key, out var list))
        {
            list = new List<Tombstone>();
            _byKey[key] = list;
        }

        if (list.Contains(tombstone))
            return false;

        list.Add(tombstone);
        return true;
    }

    /// <summary>
    /// 判定 timestamp 是否被数组中任意墓碑覆盖。
    /// 对小集合（≤ 4）线性扫描；超过 4 时仍线性扫描（v1 简化，通常墓碑数量很少）。
    /// </summary>
    private static bool IsCoveredByArray(long timestamp, Tombstone[] bucket)
    {
        for (int i = 0; i < bucket.Length; i++)
        {
            ref readonly var tomb = ref bucket[i];
            if (timestamp >= tomb.FromTimestamp && timestamp <= tomb.ToTimestamp)
                return true;
        }
        return false;
    }

    /// <summary>重建全量只读快照，调用方必须持有 _lock。</summary>
    private void RebuildSnapshot()
    {
        var all = new List<Tombstone>();
        foreach (var list in _byKey.Values)
            all.AddRange(list);
        Volatile.Write(ref _allSnapshot, all.AsReadOnly());
    }
}
