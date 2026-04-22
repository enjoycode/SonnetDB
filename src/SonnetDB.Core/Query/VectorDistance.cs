namespace SonnetDB.Query;

/// <summary>
/// 向量距离计算工具。
/// </summary>
internal static class VectorDistance
{
    /// <summary>
    /// 按指定度量计算两个向量的距离。
    /// </summary>
    /// <param name="metric">距离度量方式。</param>
    /// <param name="a">左侧向量。</param>
    /// <param name="b">右侧向量。</param>
    /// <returns>距离值，越小表示越相似。</returns>
    public static double Compute(KnnMetric metric, ReadOnlySpan<float> a, ReadOnlySpan<float> b)
        => metric switch
        {
            KnnMetric.L2 => ComputeL2(a, b),
            KnnMetric.InnerProduct => ComputeNegativeInnerProduct(a, b),
            _ => ComputeCosine(a, b),
        };

    /// <summary>余弦距离：1 − (a·b) / (‖a‖ · ‖b‖)，值域 [0, 2]。</summary>
    public static double ComputeCosine(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        double dot = 0;
        double normA2 = 0;
        double normB2 = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += (double)a[i] * b[i];
            normA2 += (double)a[i] * a[i];
            normB2 += (double)b[i] * b[i];
        }

        if (normA2 == 0 || normB2 == 0)
            return 1.0;
        return 1.0 - dot / (Math.Sqrt(normA2) * Math.Sqrt(normB2));
    }

    /// <summary>L2（欧几里得）距离：√(Σ(aᵢ − bᵢ)²)。</summary>
    public static double ComputeL2(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        double sumSq = 0;
        for (int i = 0; i < a.Length; i++)
        {
            double delta = (double)a[i] - b[i];
            sumSq += delta * delta;
        }
        return Math.Sqrt(sumSq);
    }

    /// <summary>负内积：−(a·b)，值越小表示点积越大（越相似）。</summary>
    public static double ComputeNegativeInnerProduct(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        double dot = 0;
        for (int i = 0; i < a.Length; i++)
            dot += (double)a[i] * b[i];
        return -dot;
    }
}
