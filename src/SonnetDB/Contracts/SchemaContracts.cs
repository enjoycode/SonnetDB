namespace SonnetDB.Contracts;

/// <summary>数据库 schema 响应（供前端 SQL 自动补全和 AI 系统提示使用）。</summary>
public sealed record SchemaResponse(List<MeasurementInfo> Measurements);

/// <summary>一个 Measurement 的 schema 信息。</summary>
public sealed record MeasurementInfo(string Name, List<ColumnInfo> Columns);

/// <summary>一列的 schema 信息。</summary>
public sealed record ColumnInfo(string Name, string Role, string DataType);
