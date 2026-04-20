using System.Collections.ObjectModel;
using TSLite.Storage.Format;

namespace TSLite.Catalog;

/// <summary>
/// 一个 measurement 的 schema 定义：包含列定义（按声明顺序）以及按名查找索引。
/// 不可变值对象；通过 <see cref="Create"/> 校验后构造。
/// </summary>
public sealed class MeasurementSchema
{
    private readonly Dictionary<string, MeasurementColumn> _byName;

    /// <summary>Measurement 名称（区分大小写，非空且不含保留字符）。</summary>
    public string Name { get; }

    /// <summary>列定义列表（按 CREATE 语句中的声明顺序）。</summary>
    public IReadOnlyList<MeasurementColumn> Columns { get; }

    /// <summary>Schema 创建时间（UTC Ticks）。</summary>
    public long CreatedAtUtcTicks { get; }

    private MeasurementSchema(string name, IReadOnlyList<MeasurementColumn> columns, long createdAtUtcTicks)
    {
        Name = name;
        Columns = columns;
        CreatedAtUtcTicks = createdAtUtcTicks;
        _byName = new Dictionary<string, MeasurementColumn>(columns.Count, StringComparer.Ordinal);
        foreach (var col in columns)
            _byName[col.Name] = col;
    }

    /// <summary>
    /// 创建并校验一个新的 <see cref="MeasurementSchema"/>。
    /// </summary>
    /// <param name="name">measurement 名称。</param>
    /// <param name="columns">列定义，至少包含一列且至少一个 <see cref="MeasurementColumnRole.Field"/>。</param>
    /// <param name="createdAtUtcTicks">创建时间（UTC Ticks）；省略时使用当前时间。</param>
    /// <returns>校验通过的 <see cref="MeasurementSchema"/>。</returns>
    /// <exception cref="ArgumentException">校验失败时抛出。</exception>
    public static MeasurementSchema Create(
        string name,
        IReadOnlyList<MeasurementColumn> columns,
        long? createdAtUtcTicks = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(columns);
        if (columns.Count == 0)
            throw new ArgumentException("Measurement schema 至少需要一列。", nameof(columns));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var fieldCount = 0;
        var copy = new List<MeasurementColumn>(columns.Count);

        foreach (var col in columns)
        {
            ArgumentNullException.ThrowIfNull(col);
            if (string.IsNullOrWhiteSpace(col.Name))
                throw new ArgumentException("列名不能为空。", nameof(columns));
            if (!seen.Add(col.Name))
                throw new ArgumentException($"重复的列名 '{col.Name}'。", nameof(columns));
            if (col.Role == MeasurementColumnRole.Tag && col.DataType != FieldType.String)
                throw new ArgumentException(
                    $"Tag 列 '{col.Name}' 必须是 STRING 类型，但声明为 {col.DataType}。", nameof(columns));
            if (col.DataType == FieldType.Unknown)
                throw new ArgumentException($"列 '{col.Name}' 的数据类型不能为 Unknown。", nameof(columns));
            if (col.Role == MeasurementColumnRole.Field)
                fieldCount++;
            copy.Add(col);
        }

        if (fieldCount == 0)
            throw new ArgumentException(
                "Measurement schema 至少需要一个 FIELD 列。", nameof(columns));

        return new MeasurementSchema(
            name,
            new ReadOnlyCollection<MeasurementColumn>(copy),
            createdAtUtcTicks ?? DateTime.UtcNow.Ticks);
    }

    /// <summary>按名查找列；未命中返回 null。</summary>
    /// <param name="columnName">列名（区分大小写）。</param>
    public MeasurementColumn? TryGetColumn(string columnName)
    {
        ArgumentNullException.ThrowIfNull(columnName);
        return _byName.GetValueOrDefault(columnName);
    }

    /// <summary>枚举所有 Tag 列（按声明顺序）。</summary>
    public IEnumerable<MeasurementColumn> TagColumns
        => Columns.Where(c => c.Role == MeasurementColumnRole.Tag);

    /// <summary>枚举所有 Field 列（按声明顺序）。</summary>
    public IEnumerable<MeasurementColumn> FieldColumns
        => Columns.Where(c => c.Role == MeasurementColumnRole.Field);
}
