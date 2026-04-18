namespace TSLite.Storage.Format;

/// <summary>
/// Block 数据载荷的压缩/编码方式。
/// </summary>
public enum BlockEncoding : byte
{
    /// <summary>原始无压缩（plain bytes）。</summary>
    None = 0,

    /// <summary>时间戳 delta 编码（Milestone 6 启用）。</summary>
    DeltaTimestamp = 1,

    /// <summary>值列 XOR / delta-of-delta 编码（Milestone 6 启用）。</summary>
    DeltaValue = 2,
}
