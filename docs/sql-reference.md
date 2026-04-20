---
layout: default
title: SQL 参考
description: 覆盖当前服务端支持的 measurement 定义、写入、查询、删除和批量入口。
permalink: /sql-reference/
---

## 建表

定义 measurement schema：

```sql
CREATE MEASUREMENT cpu (
    host TAG,
    region TAG,
    usage FLOAT64 FIELD,
    temperature FLOAT64 FIELD,
    ts TIMESTAMP
);
```

## 单条或批量写入

```sql
INSERT INTO cpu(host, region, usage, temperature, ts)
VALUES
('server-01', 'cn-hz', 0.71, 63.5, 1713676800000),
('server-02', 'cn-sh', 0.64, 58.2, 1713676860000);
```

## 时间范围查询

```sql
SELECT host, region, usage, temperature, ts
FROM cpu
WHERE ts >= 1713676800000 AND ts < 1713677400000;
```

## 聚合查询

```sql
SELECT host, avg(usage), max(temperature)
FROM cpu
WHERE ts >= 1713676800000 AND ts < 1713680400000
GROUP BY host, time(1m);
```

当前聚合能力覆盖：

- `count`
- `sum`
- `min`
- `max`
- `avg`
- `first`
- `last`

## 删除

```sql
DELETE FROM cpu
WHERE ts >= 1713676800000 AND ts <= 1713677400000;
```

删除在底层通过 tombstone 与 compaction 消化，不会直接就地改写旧 segment。

## 服务端 SQL 端点

| 端点 | 用途 |
| --- | --- |
| `POST /v1/db/{db}/sql` | 提交单条 SQL |
| `POST /v1/db/{db}/sql/batch` | 提交批量 SQL |
| `POST /v1/sql` | 控制面 SQL，仅 `admin` 可用 |

## 批量写入快路径

为了绕开传统 SQL parser 的开销，`TSLite.Server` 额外提供三类批量入口：

| 端点 | 格式 |
| --- | --- |
| `POST /v1/db/{db}/measurements/{m}/lp` | Influx Line Protocol 子集 |
| `POST /v1/db/{db}/measurements/{m}/json` | JSON points |
| `POST /v1/db/{db}/measurements/{m}/bulk` | `INSERT INTO ... VALUES (...)` 快路径 |

这些端点统一返回：

```json
{
  "writtenRows": 1024,
  "skippedRows": 0,
  "elapsedMilliseconds": 12
}
```

## 鉴权说明

- `readonly` 只能读
- `readwrite` 可以写入和查询
- `admin` 额外拥有控制面能力，例如用户、授权和数据库管理
