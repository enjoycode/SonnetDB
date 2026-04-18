namespace TSLite.Storage.Format;

/// <summary>
/// WAL 记录类型，用于区分 WAL 日志中不同操作的条目。
/// </summary>
public enum WalRecordType : byte
{
    /// <summary>未知记录类型（占位，不应出现在有效 WAL 中）。</summary>
    Unknown = 0,

    /// <summary>数据写入记录（包含一个时序数据点）。</summary>
    Write = 1,

    /// <summary>检查点记录（标记 Flush 完成后的截断位置）。</summary>
    Checkpoint = 2,

    /// <summary>序列目录更新记录（新建 SeriesId 映射）。</summary>
    CatalogUpdate = 3,
}
