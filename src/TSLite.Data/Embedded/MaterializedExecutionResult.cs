using TSLite.Data.Internal;
using TSLite.Sql.Execution;

namespace TSLite.Data.Embedded;

/// <summary>
/// 把内存物化的 <see cref="SelectExecutionResult"/> 适配为 <see cref="IExecutionResult"/>；
/// 也用于非 SELECT 语句返回受影响行数。
/// </summary>
internal sealed class MaterializedExecutionResult : IExecutionResult
{
    private readonly IReadOnlyList<IReadOnlyList<object?>> _rows;
    private readonly Type[] _columnTypes;
    private int _rowIndex = -1;

    private MaterializedExecutionResult(
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyList<object?>> rows,
        int recordsAffected)
    {
        Columns = columns;
        _rows = rows;
        RecordsAffected = recordsAffected;
        _columnTypes = new Type[columns.Count];
        for (int c = 0; c < columns.Count; c++)
        {
            Type t = typeof(object);
            for (int r = 0; r < rows.Count; r++)
            {
                var v = rows[r][c];
                if (v is null) continue;
                t = v.GetType();
                break;
            }
            _columnTypes[c] = t;
        }
    }

    public int RecordsAffected { get; }

    public IReadOnlyList<string> Columns { get; }

    public bool ReadNextRow()
    {
        if (_rowIndex + 1 >= _rows.Count) return false;
        _rowIndex++;
        return true;
    }

    public object? GetValue(int ordinal)
    {
        if (_rowIndex < 0 || _rowIndex >= _rows.Count)
            throw new InvalidOperationException("当前未定位到任何行。");
        return _rows[_rowIndex][ordinal];
    }

    public Type GetFieldType(int ordinal) => _columnTypes[ordinal];

    public void Dispose() { /* 无非托管资源 */ }

    public static MaterializedExecutionResult FromSelect(SelectExecutionResult result)
        => new(result.Columns, result.Rows, recordsAffected: -1);

    public static MaterializedExecutionResult NonQuery(int recordsAffected)
        => new(Array.Empty<string>(), Array.Empty<IReadOnlyList<object?>>(), recordsAffected);
}
