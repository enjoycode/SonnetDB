namespace SonnetDB.Storage.Segments;

/// <summary>
/// 单个段文件的内存索引。
/// <list type="bullet">
///   <item><description><c>byId</c>：SeriesId → 该段中属于此 series 的 <see cref="BlockDescriptor"/> 列表（按 MinTimestamp 升序）。</description></item>
///   <item><description><c>byField</c>：(SeriesId, FieldName) → <see cref="BlockDescriptor"/> 列表（按 MinTimestamp 升序）。</description></item>
///   <item><description><c>timeRange</c>：段级 (Min, Max) 时间范围，用于快速时间过滤剪枝。</description></item>
/// </list>
/// </summary>
public sealed class SegmentIndex
{
    private readonly Dictionary<ulong, List<BlockDescriptor>> _byId;
    private readonly Dictionary<(ulong SeriesId, string FieldName), List<BlockDescriptor>> _byField;

    /// <summary>所属段的唯一标识符。</summary>
    public long SegmentId { get; }

    /// <summary>所属段的文件路径。</summary>
    public string SegmentPath { get; }

    /// <summary>段内所有 Block 的最小时间戳（毫秒 UTC）；无 Block 时为 <see cref="long.MaxValue"/>。</summary>
    public long MinTimestamp { get; }

    /// <summary>段内所有 Block 的最大时间戳（毫秒 UTC）；无 Block 时为 <see cref="long.MinValue"/>。</summary>
    public long MaxTimestamp { get; }

    /// <summary>本段索引覆盖的 Block 总数。</summary>
    public int BlockCount { get; }

    private SegmentIndex(
        long segmentId,
        string segmentPath,
        long minTimestamp,
        long maxTimestamp,
        int blockCount,
        Dictionary<ulong, List<BlockDescriptor>> byId,
        Dictionary<(ulong, string), List<BlockDescriptor>> byField)
    {
        SegmentId = segmentId;
        SegmentPath = segmentPath;
        MinTimestamp = minTimestamp;
        MaxTimestamp = maxTimestamp;
        BlockCount = blockCount;
        _byId = byId;
        _byField = byField;
    }

    /// <summary>
    /// 遍历 <paramref name="reader"/> 的所有 Block，构建单段内存索引。
    /// </summary>
    /// <param name="reader">已打开的段读取器。</param>
    /// <param name="segmentId">所属段唯一标识符。</param>
    /// <returns>构建完成的 <see cref="SegmentIndex"/> 实例。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> 为 null 时抛出。</exception>
    public static SegmentIndex Build(SegmentReader reader, long segmentId)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var byId = new Dictionary<ulong, List<BlockDescriptor>>();
        var byField = new Dictionary<(ulong, string), List<BlockDescriptor>>();

        foreach (var block in reader.Blocks)
        {
            // byId
            if (!byId.TryGetValue(block.SeriesId, out var idList))
            {
                idList = new List<BlockDescriptor>();
                byId[block.SeriesId] = idList;
            }
            idList.Add(block);

            // byField
            var key = (block.SeriesId, block.FieldName);
            if (!byField.TryGetValue(key, out var fieldList))
            {
                fieldList = new List<BlockDescriptor>();
                byField[key] = fieldList;
            }
            fieldList.Add(block);
        }

        // 保险起见按 MinTimestamp 升序排列（写入顺序通常已满足，但不假设）
        foreach (var list in byId.Values)
            list.Sort(static (a, b) => a.MinTimestamp.CompareTo(b.MinTimestamp));
        foreach (var list in byField.Values)
            list.Sort(static (a, b) => a.MinTimestamp.CompareTo(b.MinTimestamp));

        return new SegmentIndex(
            segmentId,
            reader.Path,
            reader.MinTimestamp,
            reader.MaxTimestamp,
            reader.BlockCount,
            byId,
            byField);
    }

    /// <summary>
    /// 按 <paramref name="seriesId"/> 取候选 Block；未命中返回空列表（不分配）。
    /// </summary>
    /// <param name="seriesId">目标序列 ID。</param>
    /// <returns>属于该序列的 <see cref="BlockDescriptor"/> 只读列表（按 MinTimestamp 升序）。</returns>
    public IReadOnlyList<BlockDescriptor> GetBlocks(ulong seriesId)
    {
        return _byId.TryGetValue(seriesId, out var list) ? list : Array.Empty<BlockDescriptor>();
    }

    /// <summary>
    /// 按 (<paramref name="seriesId"/>, <paramref name="fieldName"/>) 取候选 Block；未命中返回空列表（不分配）。
    /// </summary>
    /// <param name="seriesId">目标序列 ID。</param>
    /// <param name="fieldName">目标字段名。</param>
    /// <returns>属于该序列+字段的 <see cref="BlockDescriptor"/> 只读列表（按 MinTimestamp 升序）。</returns>
    public IReadOnlyList<BlockDescriptor> GetBlocks(ulong seriesId, string fieldName)
    {
        ArgumentNullException.ThrowIfNull(fieldName);
        return _byField.TryGetValue((seriesId, fieldName), out var list)
            ? list
            : Array.Empty<BlockDescriptor>();
    }

    /// <summary>
    /// 按 (<paramref name="seriesId"/>, <paramref name="fieldName"/>, [<paramref name="from"/>, <paramref name="toInclusive"/>])
    /// 取与时间窗有重叠的 Block。
    /// </summary>
    /// <param name="seriesId">目标序列 ID。</param>
    /// <param name="fieldName">目标字段名。</param>
    /// <param name="from">查询起始时间戳（毫秒，inclusive）。</param>
    /// <param name="toInclusive">查询结束时间戳（毫秒，inclusive）。</param>
    /// <returns>与时间窗 [<paramref name="from"/>, <paramref name="toInclusive"/>] 相交的 <see cref="BlockDescriptor"/> 列表。</returns>
    public IReadOnlyList<BlockDescriptor> GetBlocks(ulong seriesId, string fieldName, long from, long toInclusive)
    {
        ArgumentNullException.ThrowIfNull(fieldName);

        if (!_byField.TryGetValue((seriesId, fieldName), out var list))
            return Array.Empty<BlockDescriptor>();

        // 二分定位上界：第一个 MinTimestamp > toInclusive 的索引
        int upper = FindUpperBound(list, toInclusive);
        if (upper == 0)
            return Array.Empty<BlockDescriptor>();

        // 顺序扫描 [0, upper)，进一步过滤 MaxTimestamp >= from
        var result = new List<BlockDescriptor>(upper);
        for (int i = 0; i < upper; i++)
        {
            if (list[i].MaxTimestamp >= from)
                result.Add(list[i]);
        }

        return result.Count == 0 ? Array.Empty<BlockDescriptor>() : result;
    }

    /// <summary>
    /// 段时间范围与 [<paramref name="from"/>, <paramref name="toInclusive"/>] 是否相交。
    /// </summary>
    /// <param name="from">查询起始时间戳（毫秒，inclusive）。</param>
    /// <param name="toInclusive">查询结束时间戳（毫秒，inclusive）。</param>
    /// <returns>若相交返回 <c>true</c>，否则返回 <c>false</c>。</returns>
    public bool OverlapsTimeRange(long from, long toInclusive)
    {
        return MinTimestamp <= toInclusive && MaxTimestamp >= from;
    }

    /// <summary>
    /// 在已按 MinTimestamp 升序排列的列表中，二分查找第一个 MinTimestamp &gt; <paramref name="toInclusive"/> 的索引（上界）。
    /// </summary>
    private static int FindUpperBound(List<BlockDescriptor> sorted, long toInclusive)
    {
        int lo = 0, hi = sorted.Count;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (sorted[mid].MinTimestamp <= toInclusive)
                lo = mid + 1;
            else
                hi = mid;
        }
        return lo;
    }
}
