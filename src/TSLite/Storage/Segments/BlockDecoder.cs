using System.Buffers.Binary;
using System.Text;
using TSLite.Model;
using TSLite.Storage.Format;

namespace TSLite.Storage.Segments;

/// <summary>
/// <see cref="ValuePayloadCodec"/> 的对偶：从二进制载荷解码出 <see cref="DataPoint"/> 序列。
/// 所有数值通过 <see cref="BinaryPrimitives"/> LE 读取，保证跨平台一致性。
/// </summary>
internal static class BlockDecoder
{
    /// <summary>
    /// 解码指定 Block 的全部 DataPoint。
    /// </summary>
    /// <param name="d">Block 描述符（含 Count / FieldType）。</param>
    /// <param name="tsPayload">时间戳载荷字节视图。</param>
    /// <param name="valPayload">值载荷字节视图。</param>
    /// <returns>按时间戳升序排列的 DataPoint 数组。</returns>
    public static DataPoint[] Decode(
        in BlockDescriptor d,
        ReadOnlySpan<byte> tsPayload,
        ReadOnlySpan<byte> valPayload)
    {
        int count = d.Count;
        if (count == 0)
            return [];

        var result = new DataPoint[count];
        ReadTimestamps(d.TimestampEncoding, tsPayload, count, result);
        ReadValues(d.FieldType, valPayload, count, result);
        return result;
    }

    /// <summary>
    /// 解码 Block 内 [<paramref name="from"/>, <paramref name="toInclusive"/>] 时间范围的 DataPoint。
    /// </summary>
    /// <param name="d">Block 描述符。</param>
    /// <param name="tsPayload">时间戳载荷字节视图。</param>
    /// <param name="valPayload">值载荷字节视图。</param>
    /// <param name="from">起始时间戳（含）。</param>
    /// <param name="toInclusive">结束时间戳（含）。</param>
    /// <returns>在时间范围内的 DataPoint 数组（可能为空）。</returns>
    public static DataPoint[] DecodeRange(
        in BlockDescriptor d,
        ReadOnlySpan<byte> tsPayload,
        ReadOnlySpan<byte> valPayload,
        long from,
        long toInclusive)
    {
        int count = d.Count;
        if (count == 0)
            return [];

        // 先读所有时间戳，用二分查找确定范围
        long[] timestamps = new long[count];
        ReadTimestampsRaw(d.TimestampEncoding, tsPayload, count, timestamps);

        int start = LowerBound(timestamps, from);
        int end = UpperBound(timestamps, toInclusive);

        if (start >= end)
            return [];

        int rangeCount = end - start;
        var result = new DataPoint[rangeCount];

        // 将目标时间戳复制到 DataPoint
        for (int i = 0; i < rangeCount; i++)
            result[i] = new DataPoint(timestamps[start + i], default);

        // 解码对应范围的值
        ReadValuesRange(d.FieldType, valPayload, count, start, rangeCount, result);
        return result;
    }

    // ── 私有辅助 ──────────────────────────────────────────────────────────────

    private static void ReadTimestamps(BlockEncoding tsEncoding, ReadOnlySpan<byte> tsPayload, int count, DataPoint[] result)
    {
        if ((tsEncoding & BlockEncoding.DeltaTimestamp) != 0)
        {
            long[] tmp = new long[count];
            TimestampCodec.ReadDeltaOfDelta(tsPayload, tmp);
            for (int i = 0; i < count; i++)
                result[i] = new DataPoint(tmp[i], default);
            return;
        }

        for (int i = 0; i < count; i++)
        {
            long ts = BinaryPrimitives.ReadInt64LittleEndian(tsPayload.Slice(i * 8, 8));
            result[i] = new DataPoint(ts, default);
        }
    }

    private static void ReadTimestampsRaw(BlockEncoding tsEncoding, ReadOnlySpan<byte> tsPayload, int count, long[] timestamps)
    {
        if ((tsEncoding & BlockEncoding.DeltaTimestamp) != 0)
        {
            TimestampCodec.ReadDeltaOfDelta(tsPayload, timestamps.AsSpan(0, count));
            return;
        }

        for (int i = 0; i < count; i++)
            timestamps[i] = BinaryPrimitives.ReadInt64LittleEndian(tsPayload.Slice(i * 8, 8));
    }

    private static void ReadValues(FieldType fieldType, ReadOnlySpan<byte> valPayload, int count, DataPoint[] result)
    {
        switch (fieldType)
        {
            case FieldType.Float64:
                for (int i = 0; i < count; i++)
                {
                    double v = BinaryPrimitives.ReadDoubleLittleEndian(valPayload.Slice(i * 8, 8));
                    result[i] = new DataPoint(result[i].Timestamp, FieldValue.FromDouble(v));
                }
                break;

            case FieldType.Int64:
                for (int i = 0; i < count; i++)
                {
                    long v = BinaryPrimitives.ReadInt64LittleEndian(valPayload.Slice(i * 8, 8));
                    result[i] = new DataPoint(result[i].Timestamp, FieldValue.FromLong(v));
                }
                break;

            case FieldType.Boolean:
                for (int i = 0; i < count; i++)
                {
                    bool v = valPayload[i] != 0;
                    result[i] = new DataPoint(result[i].Timestamp, FieldValue.FromBool(v));
                }
                break;

            case FieldType.String:
                ReadStringValues(valPayload, count, result, startIndex: 0, resultOffset: 0);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(fieldType), fieldType, null);
        }
    }

    private static void ReadValuesRange(
        FieldType fieldType,
        ReadOnlySpan<byte> valPayload,
        int totalCount,
        int start,
        int rangeCount,
        DataPoint[] result)
    {
        switch (fieldType)
        {
            case FieldType.Float64:
                for (int i = 0; i < rangeCount; i++)
                {
                    double v = BinaryPrimitives.ReadDoubleLittleEndian(valPayload.Slice((start + i) * 8, 8));
                    result[i] = new DataPoint(result[i].Timestamp, FieldValue.FromDouble(v));
                }
                break;

            case FieldType.Int64:
                for (int i = 0; i < rangeCount; i++)
                {
                    long v = BinaryPrimitives.ReadInt64LittleEndian(valPayload.Slice((start + i) * 8, 8));
                    result[i] = new DataPoint(result[i].Timestamp, FieldValue.FromLong(v));
                }
                break;

            case FieldType.Boolean:
                for (int i = 0; i < rangeCount; i++)
                {
                    bool v = valPayload[start + i] != 0;
                    result[i] = new DataPoint(result[i].Timestamp, FieldValue.FromBool(v));
                }
                break;

            case FieldType.String:
                ReadStringValues(valPayload, totalCount, result, startIndex: start, resultOffset: 0);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(fieldType), fieldType, null);
        }
    }

    private static void ReadStringValues(
        ReadOnlySpan<byte> valPayload,
        int totalCount,
        DataPoint[] result,
        int startIndex,
        int resultOffset)
    {
        // 需要跳过前 startIndex 个字符串条目
        int pos = 0;
        int skipped = 0;
        int written = 0;
        int targetCount = result.Length - resultOffset;

        while (skipped + written < totalCount && written < targetCount)
        {
            int byteLen = BinaryPrimitives.ReadInt32LittleEndian(valPayload.Slice(pos, 4));
            pos += 4;

            if (skipped < startIndex)
            {
                pos += byteLen;
                skipped++;
            }
            else
            {
                string s = Encoding.UTF8.GetString(valPayload.Slice(pos, byteLen));
                pos += byteLen;
                result[resultOffset + written] = new DataPoint(
                    result[resultOffset + written].Timestamp,
                    FieldValue.FromString(s));
                written++;
            }
        }
    }

    /// <summary>二分查找：第一个 timestamps[i] >= value 的位置。</summary>
    private static int LowerBound(long[] timestamps, long value)
    {
        int lo = 0, hi = timestamps.Length;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (timestamps[mid] < value)
                lo = mid + 1;
            else
                hi = mid;
        }
        return lo;
    }

    /// <summary>二分查找：第一个 timestamps[i] > value 的位置（即上界 exclusive end）。</summary>
    private static int UpperBound(long[] timestamps, long value)
    {
        int lo = 0, hi = timestamps.Length;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (timestamps[mid] <= value)
                lo = mid + 1;
            else
                hi = mid;
        }
        return lo;
    }
}
