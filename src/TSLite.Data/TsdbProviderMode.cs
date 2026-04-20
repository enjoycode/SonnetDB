namespace TSLite.Data;

/// <summary>
/// TSLite ADO.NET 提供程序的运行模式。由 <see cref="TsdbConnectionStringBuilder"/>
/// 根据连接字符串自动推断；也可显式指定。
/// </summary>
public enum TsdbProviderMode
{
    /// <summary>嵌入式：直接打开本地目录上的 <see cref="TSLite.Engine.Tsdb"/> 引擎。</summary>
    Embedded = 0,

    /// <summary>远程：通过 HTTP 调用 <c>TSLite.Server</c>，以 ndjson 流式接收结果。</summary>
    Remote = 1,
}
