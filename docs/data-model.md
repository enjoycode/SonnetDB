---
layout: default
title: 数据模型
description: 了解 TSLite 中 measurement、tags、fields、series 和 timestamp 的组织方式。
permalink: /data-model/
---

## 核心概念

TSLite 的逻辑模型和常见时序数据库接近，可以把一条数据理解为：

- 一个 measurement
- 一组 tags
- 一组 fields
- 一个 timestamp

示意如下：

```text
measurement: cpu
tags: host=server-01, region=cn-hz
fields: usage=0.71, temperature=63.5
timestamp: 1713676800000
```

## measurement

measurement 可以理解为一类点位或一张时序表，例如：

- `cpu`
- `memory`
- `power_meter`
- `weather`

在 SQL 层，measurement 通过 `CREATE MEASUREMENT` 定义 schema。

## tags

tags 用于描述序列身份，适合放：

- 设备编号
- 主机名
- 地域
- 业务维度

TSLite 会基于 `measurement + sorted(tags)` 规范化生成 `SeriesKey`，再计算 `SeriesId`。这意味着：

- 相同 measurement
- 相同 tag 集合
- 仅 tag 顺序不同

仍然会归并到同一个时间序列。

## fields

fields 是真正随时间变化的观测值。当前核心类型包括：

- `Float64`
- `Int64`
- `Boolean`
- `String`

通常建议：

- tags 放筛选维度
- fields 放采样值

## timestamp

时间戳是时序查询的主轴。无论是嵌入式 API 还是 SQL，查询都会围绕时间范围展开。

## 数据库层级

在 `TSLite.Server` 中，一个数据库对应一个独立的数据目录和一个 `Tsdb` 实例。你可以把它理解为“同一个服务中的多租户容器”。

## 适合的建模方式

推荐：

- 用 measurement 表示业务对象类型
- 用 tags 表示高选择性的过滤维度
- 用 fields 表示数值或状态
- 保持同一 measurement 的 schema 清晰稳定

不推荐：

- 把高频变化的大文本放进 tag
- 让同名 field 在同一 measurement 下频繁漂移类型
- 把时间戳拆成普通字段参与查询
