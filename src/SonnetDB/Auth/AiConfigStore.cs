using SonnetDB.Configuration;

namespace SonnetDB.Auth;

/// <summary>
/// AI 助手配置持久化存储。文件位于 <c>{systemDirectory}/ai-config.json</c>。
/// 线程安全（基于 <see cref="Lock"/>）。
/// </summary>
public sealed class AiConfigStore
{
    private readonly string _filePath;
    private readonly Lock _lock = new();
    private AiOptions _state;

    /// <summary>
    /// 在指定系统目录下打开（或初始化）AI 配置存储。
    /// </summary>
    public AiConfigStore(string systemDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(systemDirectory);
        Directory.CreateDirectory(systemDirectory);
        _filePath = Path.Combine(systemDirectory, "ai-config.json");
        _state = AtomicJsonFile.Read(_filePath, AuthJsonContext.Default.AiOptions, () => new AiOptions());
    }

    /// <summary>获取当前 AI 配置快照。</summary>
    public AiOptions Get()
    {
        lock (_lock) return _state;
    }

    /// <summary>保存新配置并持久化到磁盘。</summary>
    public void Save(AiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        lock (_lock)
        {
            _state = options;
            AtomicJsonFile.Write(_filePath, _state, AuthJsonContext.Default.AiOptions);
        }
    }
}
