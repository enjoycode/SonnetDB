namespace TSLite.Storage.Format;

/// <summary>
/// Block 数据载荷的压缩/编码方式。
/// <para>
/// 这是一个位标志枚举：低位表示时间戳列编码，高位表示值列编码，
/// 同一 Block 可以同时启用两侧编码（例如 <c>DeltaTimestamp | DeltaValue</c>）。
/// </para>
/// </summary>
[Flags]
public enum BlockEncoding : byte
{
    /// <summary>原始无压缩（两侧 plain bytes）。</summary>
    None = 0,

    /// <summary>时间戳列采用 delta-of-delta + zigzag varint 编码（Milestone 7 PR #29 启用）。</summary>
    DeltaTimestamp = 1,

    /// <summary>值列采用 XOR / Gorilla 等编码（Milestone 7 PR #30 启用）。</summary>
    DeltaValue = 2,
}
