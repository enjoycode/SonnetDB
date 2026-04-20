---
layout: default
title: "数据模型"
description: "了解 measurement、tag、field、time、series 与数据库层级的真实关系。"
permalink: /data-model/
---

## 核心概念

TSLite 中的一条时序数据由四个部分组成：

- 一个 `measurement`
- 一组 `tags`
- 一组 `fields`
- 一个 `time` 时间戳

例如：

```text
measurement: cpu
tags: host=server-01, region=cn-hz
fields: usage=0.71, temperature=63.5
time: 1713676800000
```

## measurement

`measurement` 表示一类时序对象，例如：

- `cpu`
- `memory`
- `power_meter`
- `weather`

在 SQL 中，measurement 通过 `CREATE MEASUREMENT` 定义 schema：

```sql
CREATE MEASUREMENT cpu (
    host TAG,
    region TAG,
    usage FIELD FLOAT,
    temperature FIELD FLOAT,
    healthy FIELD BOOL
)
```

说明：

- measurement 有显式 schema。
- schema 中必须至少包含一个 `FIELD` 列。
- `TAG` 列只能是字符串类型。
- `time` 不是 schema 里声明的普通列，而是保留时间列。

## tags

tag 用来表示序列身份和过滤维度，例如：

- 主机名
- 设备编号
- 区域
- 租户或站点

TSLite 会使用 `measurement + sorted(tags)` 规范化出逻辑序列键。因此：

- 相同 measurement
- 相同 tag 集合
- 仅 tag 顺序不同

仍然会被视为同一个 series。

适合放在 tag 的内容：

- 过滤条件
- 分片维度
- 低频变化的标识信息

不适合放在 tag 的内容：

- 高频变化的大文本
- 采样值本身
- 会频繁改变类型的字段

## fields

field 是真正随时间变化的观测值。当前支持：

- `FLOAT`
- `INT`
- `BOOL`
- `STRING`

同一个 measurement 中，field 的名称和类型应该保持稳定。SQL 查询和 ADO.NET 读取会依赖这一点推断结果列类型。

## time

`time` 是保留时间列：

- `INSERT` 时可通过 `time` 指定 Unix 毫秒时间戳
- `SELECT` 时可直接投影和过滤 `time`
- `DELETE` 时可用 `time` 指定删除窗口

`time` 不需要在 `CREATE MEASUREMENT` 中声明。

例如：

```sql
INSERT INTO cpu (time, host, usage)
VALUES (1713676800000, 'server-01', 0.71)
```

如果 `INSERT` 未提供 `time`，系统会使用当前 UTC 毫秒时间。

## series

逻辑上可以把每个 series 理解成：

```text
measurement + ordered tags
```

例如：

```text
cpu{host=server-01, region=cn-hz}
cpu{host=server-02, region=cn-hz}
```

这两个 series 共享同一个 measurement schema，但属于两条不同的时序序列。

## 行与点的关系

SQL 看起来像“表和行”，底层仍然是按 series 和 field 存储的时序点。

举例：

```sql
INSERT INTO cpu (time, host, usage, temperature)
VALUES (1000, 'server-01', 0.71, 63.5)
```

可以理解为：

- measurement: `cpu`
- tags: `host=server-01`
- fields:
  - `usage=0.71`
  - `temperature=63.5`
- time: `1000`

如果不同时间戳写入的 field 列不完全一致，`SELECT` 时缺失字段会以 `NULL` 返回。

## 数据库层级

在 `TSLite.Server` 中，一个数据库对应一个独立的数据目录和一个 `Tsdb` 实例：

```text
<data-root>/
├─ .system/
├─ metrics/
├─ telemetry/
└─ archive/
```

每个数据库内部都有自己独立的：

- `measurements.tslschema`
- `catalog.tslcat`
- `tombstones.tslmanifest`
- `wal/`
- `segments/`

## 建模建议

- 用 measurement 表示业务对象类型，不要一个 measurement 混太多无关概念。
- 用 tag 表示筛选和身份维度。
- 用 field 表示采样值和状态值。
- 统一时间精度，当前默认以 Unix 毫秒表达。
- 保持 schema 稳定，减少同名 field 的类型漂移。
