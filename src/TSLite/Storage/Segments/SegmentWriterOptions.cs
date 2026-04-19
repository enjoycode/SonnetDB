namespace TSLite.Storage.Segments;

/// <summary>
/// <see cref="SegmentWriter"/> 的构建选项。
/// </summary>
public sealed class SegmentWriterOptions
{
    /// <summary>底层 <see cref="System.IO.BufferedStream"/> 缓冲区大小，默认 64 KiB。</summary>
    public int BufferSize { get; init; } = 64 * 1024;

    /// <summary>是否在 Commit 时执行 fsync（<c>Flush(true)</c>）。默认 <c>true</c>。</summary>
    public bool FsyncOnCommit { get; init; } = true;

    /// <summary>临时文件后缀，默认 <c>".tmp"</c>。</summary>
    public string TempFileSuffix { get; init; } = ".tmp";

    /// <summary>
    /// 崩溃注入钩子（仅供测试）：在写入指定字节偏移时被调用，可抛出异常以模拟崩溃。
    /// </summary>
    internal Action<long>? FailAt { get; init; }

    /// <summary>默认选项实例。</summary>
    public static SegmentWriterOptions Default { get; } = new();
}
