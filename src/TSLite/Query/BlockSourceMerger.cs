using TSLite.Model;

namespace TSLite.Query;

/// <summary>
/// 多路有序合并器：将 MemTable 切片与多个 Segment 解码后的 Block 切片按时间戳升序合并为
/// 单个 <see cref="DataPoint"/> 流。
/// </summary>
internal static class BlockSourceMerger
{
    /// <summary>
    /// 按时间戳升序合并 MemTable 切片与多个 Segment Block 切片。
    /// <para>
    /// 合并策略（N 路最小堆）：
    /// <list type="bullet">
    ///   <item><description>每路持有一个迭代器游标（InputIndex, CurrentIndex）。</description></item>
    ///   <item><description>最小堆节点按 (Timestamp, InputIndex) 排序，InputIndex 越小优先级越高（段按 SegmentId 升序排列，MemTable 在最末）。</description></item>
    ///   <item><description>时间戳相同时保持稳定：先段（SegmentId 升序），最后 MemTable。</description></item>
    ///   <item><description>v1 不去重：同时间戳多源全部 yield。</description></item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="memTableSlice">MemTable 切片（已按时间升序排列）；null 表示无 MemTable 数据。</param>
    /// <param name="segmentSlices">Segment Block 解码后的数组列表（每项已按时间升序排列，顺序对应 SegmentId 升序）。</param>
    /// <returns>按时间戳升序（同 ts 则按 SegmentId 升序，再 MemTable 最后）的 DataPoint 序列。</returns>
    public static IEnumerable<DataPoint> Merge(
        ReadOnlyMemory<DataPoint>? memTableSlice,
        IReadOnlyList<DataPoint[]> segmentSlices)
    {
        // 构建输入列表：先 segment slices（顺序即 SegmentId 升序），最后 MemTable
        int totalInputs = segmentSlices.Count + (memTableSlice.HasValue ? 1 : 0);

        if (totalInputs == 0)
            yield break;

        // 特殊情况：只有一路，直接 yield
        if (totalInputs == 1)
        {
            if (segmentSlices.Count == 1)
            {
                foreach (var dp in segmentSlices[0])
                    yield return dp;
            }
            else
            {
                var mem = memTableSlice!.Value;
                for (int i = 0; i < mem.Length; i++)
                    yield return mem.Span[i];
            }
            yield break;
        }

        // 构建各路迭代器（0..segmentSlices.Count-1 为 segment，最后一个为 MemTable）
        var cursors = new int[totalInputs]; // 当前各路已消费位置
        var lengths = new int[totalInputs];

        for (int i = 0; i < segmentSlices.Count; i++)
            lengths[i] = segmentSlices[i].Length;

        if (memTableSlice.HasValue)
            lengths[totalInputs - 1] = memTableSlice.Value.Length;

        // 初始化最小堆：每路若非空则取第一个元素入堆
        // 堆节点：(Timestamp, InputIndex)
        var heap = new List<(long Timestamp, int InputIndex)>(totalInputs);

        for (int i = 0; i < totalInputs; i++)
        {
            if (lengths[i] > 0)
                heap.Add((GetTimestamp(i, 0, segmentSlices, memTableSlice), i));
        }

        BuildMinHeap(heap);

        while (heap.Count > 0)
        {
            // 取堆顶（最小时间戳，同 ts 时 InputIndex 最小优先）
            var (ts, inputIdx) = heap[0];

            // yield 当前点
            yield return GetPoint(inputIdx, cursors[inputIdx], segmentSlices, memTableSlice);

            cursors[inputIdx]++;

            // 更新堆顶：若该路还有数据，则替换堆顶；否则删除堆顶
            if (cursors[inputIdx] < lengths[inputIdx])
            {
                long nextTs = GetTimestamp(inputIdx, cursors[inputIdx], segmentSlices, memTableSlice);
                heap[0] = (nextTs, inputIdx);
            }
            else
            {
                // 将堆尾移到堆顶，缩小堆
                int last = heap.Count - 1;
                heap[0] = heap[last];
                heap.RemoveAt(last);
            }

            // 向下调整堆顶
            SiftDown(heap, 0);
        }
    }

    // ── 辅助方法 ──────────────────────────────────────────────────────────────

    private static long GetTimestamp(
        int inputIndex,
        int position,
        IReadOnlyList<DataPoint[]> segmentSlices,
        ReadOnlyMemory<DataPoint>? memTableSlice)
    {
        if (inputIndex < segmentSlices.Count)
            return segmentSlices[inputIndex][position].Timestamp;
        return memTableSlice!.Value.Span[position].Timestamp;
    }

    private static DataPoint GetPoint(
        int inputIndex,
        int position,
        IReadOnlyList<DataPoint[]> segmentSlices,
        ReadOnlyMemory<DataPoint>? memTableSlice)
    {
        if (inputIndex < segmentSlices.Count)
            return segmentSlices[inputIndex][position];
        return memTableSlice!.Value.Span[position];
    }

    /// <summary>从最后一个非叶节点开始向上构建最小堆（Floyd 建堆算法）。</summary>
    private static void BuildMinHeap(List<(long Timestamp, int InputIndex)> heap)
    {
        int n = heap.Count;
        for (int i = n / 2 - 1; i >= 0; i--)
            SiftDown(heap, i);
    }

    /// <summary>将 <paramref name="i"/> 位置的元素向下调整到正确位置（最小堆，同 ts 时 InputIndex 小的优先）。</summary>
    private static void SiftDown(List<(long Timestamp, int InputIndex)> heap, int i)
    {
        int n = heap.Count;
        while (true)
        {
            int smallest = i;
            int left = 2 * i + 1;
            int right = 2 * i + 2;

            if (left < n && IsLess(heap[left], heap[smallest]))
                smallest = left;

            if (right < n && IsLess(heap[right], heap[smallest]))
                smallest = right;

            if (smallest == i)
                break;

            (heap[i], heap[smallest]) = (heap[smallest], heap[i]);
            i = smallest;
        }
    }

    /// <summary>比较两个堆节点：先比时间戳，再比输入路索引（索引小优先）。</summary>
    private static bool IsLess(
        (long Timestamp, int InputIndex) a,
        (long Timestamp, int InputIndex) b)
    {
        if (a.Timestamp != b.Timestamp)
            return a.Timestamp < b.Timestamp;
        return a.InputIndex < b.InputIndex;
    }
}
