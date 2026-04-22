namespace SonnetDB.Catalog;

/// <summary>
/// 向量索引类型。
/// </summary>
public enum VectorIndexKind : byte
{
    /// <summary>
    /// HNSW（Hierarchical Navigable Small World）图索引。
    /// </summary>
    Hnsw = 1,
}

/// <summary>
/// HNSW 索引参数。
/// </summary>
/// <param name="M">每个节点在每层保留的最大邻接数。</param>
/// <param name="Ef">建图与查询默认使用的候选规模。</param>
public sealed record HnswVectorIndexOptions(int M, int Ef);

/// <summary>
/// Measurement 中某个 VECTOR 列的索引定义。
/// </summary>
/// <param name="Kind">索引类型。</param>
/// <param name="Hnsw">当 <see cref="Kind"/> 为 <see cref="VectorIndexKind.Hnsw"/> 时的参数。</param>
public sealed record VectorIndexDefinition(
    VectorIndexKind Kind,
    HnswVectorIndexOptions Hnsw)
{
    /// <summary>
    /// 创建 HNSW 索引定义。
    /// </summary>
    /// <param name="m">每个节点在每层保留的最大邻接数。</param>
    /// <param name="ef">建图与查询默认使用的候选规模。</param>
    /// <returns>HNSW 索引定义。</returns>
    public static VectorIndexDefinition CreateHnsw(int m, int ef)
        => new(VectorIndexKind.Hnsw, new HnswVectorIndexOptions(m, ef));
}
