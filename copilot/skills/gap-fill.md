---
name: gap-fill
description: 检测 SonnetDB 时序数据中的时间缺口（Gap Detection），以及用 NULL、前值填充、线性插值等策略补全稀疏数据，保证聚合分析的连续性。
triggers:
  - gap
  - 缺口
  - 缺失
  - 空洞
  - 填充
  - fill
  - 插值
  - 稀疏
  - 不连续
  - 数据断点
  - 前值填充
  - 线性插值
  - 零值填充
  - 数据完整性
  - 缺失数据
requires_tools:
  - query_sql
  - describe_measurement
---

# 时序缺口检测与填充指南

IoT 传感器断线、网络抖动、设备重启等场景会导致时序数据出现时间缺口。本指南介绍如何检测缺口并用合适的策略填充。

---

## 1. 缺口检测

### 1.1 检测相邻采样间隔异常

```sql
-- 找出采样间隔超过预期（正常 30 秒，超过 2 分钟视为缺口）
SELECT
    time,
    device_id,
    time - LAG(time, 1, time) OVER (ORDER BY time ASC) AS gap_ms,  -- 与上条的时间差（毫秒）
    temp_celsius
FROM sensor_climate
WHERE device_id = 'S-A01'
  AND time >= now() - 24h
ORDER BY time ASC;
-- 应用层过滤：gap_ms > 120000（2 分钟 = 120000 毫秒）即为缺口
```

### 1.2 统计缺口数量和总缺失时长

```sql
-- 统计过去 24 小时内，每台设备的缺口次数和总缺失时长
SELECT
    device_id,
    count(*)                                          AS gap_count,   -- 缺口次数
    sum(gap_ms - 30000)                               AS total_missing_ms  -- 总缺失时长（毫秒）
FROM (
    SELECT
        device_id,
        time - LAG(time, 1, time) OVER (ORDER BY time ASC) AS gap_ms
    FROM sensor_climate
    WHERE time >= now() - 24h
) AS intervals
WHERE gap_ms > 120000   -- 超过 2 分钟（正常间隔 30 秒）视为缺口
GROUP BY time(24h)      -- 按天聚合（占位，实际按 device_id 分组）
ORDER BY total_missing_ms DESC;
```

### 1.3 找出最大缺口时段

```sql
-- 找出过去 7 天内最大的数据缺口（开始时间和结束时间）
SELECT
    LAG(time, 1) OVER (ORDER BY time ASC) AS gap_start,  -- 缺口开始（上一条时间）
    time                                  AS gap_end,     -- 缺口结束（当前时间）
    time - LAG(time, 1) OVER (ORDER BY time ASC) AS gap_duration_ms
FROM sensor_climate
WHERE device_id = 'S-A01'
  AND time >= now() - 7d
ORDER BY gap_duration_ms DESC
LIMIT 10;   -- 最大的 10 个缺口
```

---

## 2. 填充策略选择

| 策略 | 适用场景 | 优点 | 缺点 |
| --- | --- | --- | --- |
| **NULL 填充** | 不确定缺失原因，保守处理 | 不引入虚假数据 | 聚合时 NULL 被忽略，影响 count |
| **前值填充（LOCF）** | 传感器断线，值未变化（如状态量） | 符合"最后已知值"语义 | 长时间缺口会引入误差 |
| **零值填充** | 计数类指标（请求数、错误数） | 语义明确（无请求=0） | 不适合连续量（温度、压力） |
| **线性插值** | 缓慢变化的物理量（温度、液位） | 平滑，接近真实值 | 剧烈变化时误差大 |
| **均值填充** | 统计分析，不关心时序连续性 | 不影响整体均值 | 破坏时序特征 |

---

## 3. 填充实现

### 3.1 前值填充（LOCF — Last Observation Carried Forward）

```sql
-- 用 LAG 取最近一个非 NULL 值填充
-- 适合：设备状态、开关量、缓慢变化的传感器
SELECT
    time,
    device_id,
    COALESCE(
        temp_celsius,
        LAG(temp_celsius IGNORE NULLS, 1) OVER (ORDER BY time ASC)
    ) AS temp_filled   -- NULL 时用前值填充
FROM sensor_climate
WHERE device_id = 'S-A01'
  AND time >= now() - 1h
ORDER BY time ASC;
```

### 3.2 线性插值

```sql
-- 线性插值：在两个已知值之间按比例估算缺失值
-- 适合：温度、压力、液位等缓慢变化的物理量
-- 实现：应用层计算（SonnetDB 当前不内置 interpolate 函数）

-- 步骤 1：查询原始数据（含 NULL）
SELECT time, temp_celsius
FROM sensor_climate
WHERE device_id = 'S-A01'
  AND time >= now() - 1h
ORDER BY time ASC;

-- 步骤 2：应用层线性插值（Python 示例）
-- import pandas as pd
-- df['temp_celsius'] = df['temp_celsius'].interpolate(method='linear')
```

### 3.3 零值填充（计数类指标）

```sql
-- 对于请求数、错误数等计数指标，缺口期间视为 0
-- 方法：生成时间序列骨架，LEFT JOIN 原始数据

-- 步骤 1：聚合到分钟桶（缺口桶自然为 NULL）
SELECT
    time_bucket(time, '1m') AS bucket,
    count(*)                AS request_count
FROM app_request
WHERE service = 'api-gateway'
  AND time >= now() - 1h
GROUP BY bucket
ORDER BY bucket ASC;
-- 缺口分钟的 bucket 不会出现在结果中

-- 步骤 2：应用层补零（Python 示例）
-- df = df.set_index('bucket').resample('1min').sum().fillna(0).reset_index()
```

---

## 4. 数据完整性验证

### 4.1 验证采样完整率

```sql
-- 计算过去 1 小时内每台设备的采样完整率
-- 期望采样点数 = 3600 秒 / 30 秒间隔 = 120 点
SELECT
    device_id,
    count(*)                                  AS actual_count,
    120                                       AS expected_count,  -- 期望点数（1小时/30秒）
    count(*) * 100.0 / 120                    AS completeness_pct -- 完整率（%）
FROM sensor_climate
WHERE workshop = 'workshop-A'
  AND time >= now() - 1h
GROUP BY time(1h)   -- 占位聚合
ORDER BY completeness_pct ASC;
-- completeness_pct < 90% 的设备需要关注
```

### 4.2 按时间桶检查数据密度

```sql
-- 检查每 5 分钟桶内的采样点数，找出稀疏时段
SELECT
    time_bucket(time, '5m') AS bucket,
    count(*)                AS sample_count,
    -- 期望：5分钟 / 30秒 = 10 个点
    CASE WHEN count(*) < 8 THEN true ELSE false END AS is_sparse
FROM sensor_climate
WHERE device_id = 'S-A01'
  AND time >= now() - 24h
GROUP BY bucket
ORDER BY bucket ASC;
```

### 4.3 检测设备长时间离线

```sql
-- 找出超过 10 分钟没有数据的设备（可能离线）
SELECT
    device_id,
    max(time) AS last_seen,
    now() - max(time) AS offline_duration_ms
FROM sensor_climate
WHERE time >= now() - 1h   -- 在最近 1 小时内有过数据的设备
GROUP BY time(1h)           -- 占位聚合
ORDER BY last_seen ASC;
-- offline_duration_ms > 600000（10 分钟）的设备视为离线
```

---

## 5. 聚合时处理缺口

```sql
-- 聚合时，缺口会导致某些时间桶没有数据
-- 这是正常的，不需要强制填充
-- 但需要在应用层处理"空桶"的展示问题

-- 查询每分钟平均温度（有缺口的分钟不会出现）
SELECT
    time_bucket(time, '1m') AS bucket,
    avg(temp_celsius)       AS avg_temp,
    count(*)                AS sample_count   -- 用于判断桶的数据质量
FROM sensor_climate
WHERE device_id = 'S-A01'
  AND time >= now() - 1h
GROUP BY bucket
ORDER BY bucket ASC;

-- 应用层处理：
-- 1. 检查相邻 bucket 的时间差，超过 2 分钟则为缺口
-- 2. 在图表中用虚线或空白表示缺口段
-- 3. 告警：缺口超过阈值时通知运维
```

---

## 6. 最佳实践

```text
✅ 写入时记录数据质量码（quality_code），区分"无数据"和"坏数据"
✅ 在聚合查询中同时输出 count(*)，用于判断桶的数据完整性
✅ 前值填充（LOCF）只适合短缺口（< 5 × 正常采样间隔）
✅ 线性插值在应用层实现，SonnetDB 负责提供原始数据
✅ 计数类指标（请求数、错误数）缺口补零，连续量（温度、压力）缺口保留 NULL

❌ 不要对长时间缺口（> 1 小时）做线性插值（误差太大）
❌ 不要在 SonnetDB 中存储插值后的数据（会混淆真实数据和估算数据）
❌ 不要忽略 quality_code 异常的数据点（坏数据比缺口更危险）
```
