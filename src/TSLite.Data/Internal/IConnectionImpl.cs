using System.Data;
using System.Data.Common;

namespace TSLite.Data.Internal;

/// <summary>
/// 内部连接实现契约。<see cref="TsdbConnection"/> 是公开 facade，
/// 真正的工作分别由 <c>EmbeddedConnectionImpl</c> 与 <c>RemoteConnectionImpl</c> 完成。
/// </summary>
internal interface IConnectionImpl : IDisposable
{
    string DataSource { get; }
    string Database { get; }
    string ServerVersion { get; }
    ConnectionState State { get; }

    void Open();
    void Close();

    /// <summary>同步执行 SQL，并将结果分发为 <see cref="IExecutionResult"/>。</summary>
    IExecutionResult Execute(string sql, TsdbParameterCollection parameters, CommandBehavior behavior);
}

/// <summary>
/// 单个 SQL 语句的执行结果，由具体连接实现产生。
/// </summary>
internal interface IExecutionResult : IDisposable
{
    /// <summary>受影响行数。INSERT/DELETE 为非负整数；SELECT 为 -1；DDL 通常为 0。</summary>
    int RecordsAffected { get; }

    /// <summary>列名（SELECT 才有；非 SELECT 返回空数组）。</summary>
    IReadOnlyList<string> Columns { get; }

    /// <summary>读取下一行，返回 <c>false</c> 表示已结束。</summary>
    bool ReadNextRow();

    /// <summary>读取指定列在当前行的值。</summary>
    object? GetValue(int ordinal);

    /// <summary>
    /// 返回指定列的运行时类型推断。嵌入式模式可预扫描全部行；
    /// 远程流式模式在未读到行之前可能返回 <see cref="object"/>。
    /// </summary>
    Type GetFieldType(int ordinal);
}
