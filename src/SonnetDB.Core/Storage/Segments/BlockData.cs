namespace SonnetDB.Storage.Segments;

/// <summary>
/// Block 的零拷贝 payload 视图（<c>readonly ref struct</c>），仅在调用栈内有效。
/// <para>
/// 三段 span 均直接指向 <see cref="SegmentReader"/> 内部 <c>byte[]</c>，不发生额外拷贝。
/// 生命周期等同于外层 <see cref="SegmentReader"/> 实例。
/// </para>
/// </summary>
public readonly ref struct BlockData
{
    /// <summary>本 Block 的元数据与物理位置描述符。</summary>
    public BlockDescriptor Descriptor { get; init; }

    /// <summary>字段名的 UTF-8 字节视图（零拷贝）。</summary>
    public ReadOnlySpan<byte> FieldNameUtf8 { get; init; }

    /// <summary>时间戳载荷的字节视图（零拷贝）。</summary>
    public ReadOnlySpan<byte> TimestampPayload { get; init; }

    /// <summary>值载荷的字节视图（零拷贝）。</summary>
    public ReadOnlySpan<byte> ValuePayload { get; init; }
}
