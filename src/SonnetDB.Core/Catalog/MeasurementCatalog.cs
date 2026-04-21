using System.Collections.Concurrent;

namespace SonnetDB.Catalog;

/// <summary>
/// 进程内 measurement schema 集合，按名建立索引。
/// 线程安全（基于 <see cref="ConcurrentDictionary{TKey,TValue}"/>）。
/// </summary>
public sealed class MeasurementCatalog
{
    private readonly ConcurrentDictionary<string, MeasurementSchema> _byName
        = new(StringComparer.Ordinal);

    /// <summary>当前已注册的 measurement 数量。</summary>
    public int Count => _byName.Count;

    /// <summary>
    /// 注册一个新的 measurement schema。若同名 schema 已存在则抛出。
    /// </summary>
    /// <param name="schema">待注册的 schema。</param>
    /// <exception cref="ArgumentNullException"><paramref name="schema"/> 为 null。</exception>
    /// <exception cref="InvalidOperationException">同名 measurement 已存在。</exception>
    public void Add(MeasurementSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (!_byName.TryAdd(schema.Name, schema))
            throw new InvalidOperationException($"Measurement '{schema.Name}' 已存在。");
    }

    /// <summary>
    /// 直接装载 schema（覆盖已有同名条目）；仅供持久化层在加载时使用。
    /// </summary>
    /// <param name="schema">待装载的 schema。</param>
    internal void LoadOrReplace(MeasurementSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        _byName[schema.Name] = schema;
    }

    /// <summary>按名查找 schema；未命中返回 null。</summary>
    /// <param name="name">measurement 名称（区分大小写）。</param>
    public MeasurementSchema? TryGet(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _byName.GetValueOrDefault(name);
    }

    /// <summary>判断指定名称的 measurement 是否已注册。</summary>
    /// <param name="name">measurement 名称。</param>
    public bool Contains(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _byName.ContainsKey(name);
    }

    /// <summary>返回当前所有 schema 的快照（按 measurement 名称的字典序排序）。</summary>
    public IReadOnlyList<MeasurementSchema> Snapshot()
    {
        var list = _byName.Values.ToList();
        list.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return list;
    }
}
