using SonnetDB.Contracts;
using SonnetDB.Engine;
using SonnetDB.Json;

namespace SonnetDB.Endpoints;

/// <summary>
/// 提供 <c>GET /v1/db/{db}/schema</c> 的响应构造逻辑。
/// </summary>
internal static class SchemaEndpointHandler
{
    /// <summary>
    /// 生成指定数据库的 schema 快照响应。
    /// </summary>
    public static IResult Handle(string db, Tsdb tsdb)
    {
        ArgumentException.ThrowIfNullOrEmpty(db);
        ArgumentNullException.ThrowIfNull(tsdb);

        var measurements = tsdb.Measurements.Snapshot();
        var infos = new List<MeasurementInfo>(measurements.Count);
        foreach (var measurement in measurements)
        {
            var columns = new List<ColumnInfo>(measurement.Columns.Count);
            foreach (var column in measurement.Columns)
                columns.Add(new ColumnInfo(column.Name, column.Role.ToString(), column.DataType.ToString()));

            infos.Add(new MeasurementInfo(measurement.Name, columns));
        }

        return Results.Json(new SchemaResponse(infos), ServerJsonContext.Default.SchemaResponse);
    }
}
