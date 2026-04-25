# SonnetDB 地理空间与轨迹

SonnetDB 支持 `GEOPOINT` 字段、GeoJSON 输出、轨迹聚合函数，以及 PR #76 引入的段内 geohash 剪枝。

## GEOPOINT 建模

```sql
CREATE MEASUREMENT vehicle (
  device TAG,
  position FIELD GEOPOINT,
  speed FIELD FLOAT
);

INSERT INTO vehicle (time, device, position, speed) VALUES
  (1000, 'truck-01', POINT(39.9042, 116.4074), 12.5),
  (2000, 'truck-01', POINT(31.2304, 121.4737), 18.0);
```

`POINT(lat, lon)` 使用纬度在前、经度在后的 SQL 字面量。HTTP ndjson 与 GeoJSON 端点输出时遵循 GeoJSON 标准坐标顺序 `[lon, lat]`。

## 空间过滤

```sql
SELECT time, position
FROM vehicle
WHERE device = 'truck-01'
  AND geo_within(position, 39.9042, 116.4074, 1000);

SELECT count(position)
FROM vehicle
WHERE geo_bbox(position, 30.0, 120.0, 32.0, 122.0);
```

- `geo_within(position, lat, lon, radius_m)` 使用 Haversine 距离判断圆形围栏。
- `geo_bbox(position, lat_min, lon_min, lat_max, lon_max)` 判断矩形外包框。
- `ST_Within` / `ST_DWithin` 是兼容别名。

## PR #76 Geohash 剪枝

Segment 格式 v5 在每个 `GEOPOINT` Block 的 `BlockHeader` 中写入：

- `GeoHashMin`：Block 内最小 32-bit geohash 前缀。
- `GeoHashMax`：Block 内最大 32-bit geohash 前缀。

当 `WHERE` 中出现 `geo_within` / `geo_bbox` 且参数是数值字面量时，查询引擎会先把过滤区域转换为 geohash 范围，并在解码前跳过明显不相交的落盘 Block。最终结果仍会逐点执行原始空间谓词，因此剪枝只影响性能，不改变查询语义。

## 轨迹 GeoJSON 端点

```http
GET /v1/db/fleet/geo/vehicle/trajectory?device=truck-01&from=1000&to=2000
GET /v1/db/fleet/geo/vehicle/trajectory?device=truck-01&format=linestring
```

默认返回 `FeatureCollection`，每个点是一条 `Feature/Point`；`format=linestring` 返回 `LineString Feature`，适合地图轨迹渲染。
