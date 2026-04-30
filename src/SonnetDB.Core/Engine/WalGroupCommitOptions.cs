namespace SonnetDB.Engine;

/// <summary>
/// WAL group-commit 配置。仅在 <see cref="TsdbOptions.SyncWalOnEveryWrite"/> 为 <c>true</c> 时生效。
/// </summary>
public sealed record WalGroupCommitOptions
{
    /// <summary>
    /// 是否启用 WAL group-commit。启用后，同一时间窗口内的多个写请求会共享一次 WAL fsync。
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// 组提交等待窗口。窗口结束后执行一次 WAL fsync，并唤醒该批次内的所有写请求。
    /// </summary>
    public TimeSpan FlushWindow { get; init; } = TimeSpan.FromMilliseconds(2);

    /// <summary>默认 WAL group-commit 配置。</summary>
    public static WalGroupCommitOptions Default { get; } = new();
}
