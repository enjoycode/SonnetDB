---
name: lag-delta
description: 在 SonnetDB 中计算时序数据的差分、变化率、环比、同比：使用 LAG/LEAD 窗口函数，适用于传感器数据趋势分析、速率计算、增量统计。
triggers:
  - lag
  - lead
  - 差分
  - 变化率
  - 增量
  - delta
  - 环比
  - 同比
  - 速率
  - rate
  - 前后对比
  - 相邻行
  - 上一条
  - 下一条
  - 变化量
  - 累计增量
requires_tools:
  - query_sql
  - describe_measurement
---

# 差分与变化率计算指南

时序数据分析中，差分（相邻值之差）和变化率（单位时间变化量）是最常用的特征工程操作，适用于传感器趋势分析、累计量转瞬时量、环比对比等场景。

---

## 1. LAG / LEAD 函数

SonnetDB 支持标准 SQL 的 LAG / LEAD 窗口函数：

```sql
LAG(<field> [, <offset> [, <default>]])   -- 取前 N 行的值
LEAD(<field> [, <offset> [, <default>]])  -- 取后 N 行的值
```

| 参数 | 说明 |
| --- | --- |
| `field` | 要取值的列名 |
| `offset` | 偏移行数，默认 1 |
| `default` | 无前/后行时的默认值，默认 NULL |

> ⚠️ 使用 LAG/LEAD 时，**必须**加 `ORDER BY time ASC` 确保时间顺序正确。

---

## 2. 一阶差分（相邻值之差）

### 场景：计算传感器读数的逐步变化量

```sql
-- 计算温度传感器每次采样的变化量
SELECT
    time,
    temp_celsius,
    temp_celsius - LAG(temp_celsius, 1, temp_celsius)
        OVER (ORDER BY time ASC)                      AS delta_temp,  -- 与上一条的差值
    time - LAG(time, 1, time)
        OVER (ORDER BY time ASC)                      AS delta_ms     -- 时间间隔（毫秒）
FROM sensor_climate
WHERE device_id = 'S-A01'
  AND time >= now() - 1h
ORDER BY time ASC;
```

### 场景：累计量转瞬时量（电表、流量计）

```sql
-- 电表 energy_kwh 是单调递增的累计量
-- 计算每个采样周期的实际用电量（瞬时功率）
SELECT
    time,
    energy_kwh,
    energy_kwh - LAG(energy_kwh, 1, energy_kwh)
        OVER (ORDER BY time ASC)                      AS delta_kwh,   -- 本周期用电量
    (energy_kwh - LAG(energy_kwh, 1, energy_kwh)
        OVER (ORDER BY time ASC))
    / ((time - LAG(time, 1, time) OVER (ORDER BY time ASC)) / 3600000.0)
                                                      AS power_kw     -- 瞬时功率（kW）
FROM sensor_power
WHERE meter_id = 'M-01'
  AND phase = 'total'
  AND time >= now() - 1h
ORDER BY time ASC;
```

---

## 3. 变化率（单位时间变化量）

### 场景：计算每秒变化速率

```sql
-- 计算 CPU 使用率的变化速率（%/秒）
SELECT
    time,
    cpu_pct,
    -- 变化率 = (当前值 - 上一值) / 时间间隔（秒）
    (cpu_pct - LAG(cpu_pct, 1, cpu_pct) OVER (ORDER BY time ASC))
    / NULLIF(
        (time - LAG(time, 1, time) OVER (ORDER BY time ASC)) / 1000.0,
        0
    )                                                  AS rate_per_sec
FROM host_cpu
WHERE host = 'server-01'
  AND time >= now() - 30m
ORDER BY time ASC;
```

### 场景：网络流量速率（bytes → bytes/sec）

```sql
-- 将累计字节数转换为瞬时速率（bytes/sec）
SELECT
    time,
    bytes_total,
    (bytes_total - LAG(bytes_total, 1, bytes_total) OVER (ORDER BY time ASC))
    / NULLIF(
        (time - LAG(time, 1, time) OVER (ORDER BY time ASC)) / 1000.0,
        0
    )                                                  AS bytes_per_sec
FROM net_interface
WHERE host = 'server-01'
  AND interface = 'eth0'
  AND time >= now() - 1h
ORDER BY time ASC;
```

---

## 4. 环比（与上一个时间桶对比）

```sql
-- 计算每小时 CPU 使用率的环比变化
-- 先聚合到小时桶，再用 LAG 计算相邻桶的差值
SELECT
    bucket,
    avg_cpu,
    LAG(avg_cpu, 1) OVER (ORDER BY bucket ASC)        AS prev_hour_cpu,
    avg_cpu - LAG(avg_cpu, 1) OVER (ORDER BY bucket ASC) AS delta_cpu,
    -- 环比变化率（%）
    (avg_cpu - LAG(avg_cpu, 1) OVER (ORDER BY bucket ASC))
    / NULLIF(LAG(avg_cpu, 1) OVER (ORDER BY bucket ASC), 0) * 100
                                                       AS change_pct
FROM (
    SELECT
        time_bucket(time, '1h') AS bucket,
        avg(cpu_pct)            AS avg_cpu
    FROM host_cpu
    WHERE host = 'server-01'
      AND time >= now() - 24h
    GROUP BY bucket
) AS hourly
ORDER BY bucket ASC;
```

---

## 5. 前后值对比（LEAD）

### 场景：预判下一步趋势

```sql
-- 同时显示当前值、上一值、下一值，用于趋势判断
SELECT
    time,
    temp_celsius                                       AS current,
    LAG(temp_celsius, 1)  OVER (ORDER BY time ASC)   AS prev,
    LEAD(temp_celsius, 1) OVER (ORDER BY time ASC)   AS next,
    -- 趋势方向：1=上升，-1=下降，0=持平
    CASE
        WHEN LEAD(temp_celsius, 1) OVER (ORDER BY time ASC) > temp_celsius THEN 1
        WHEN LEAD(temp_celsius, 1) OVER (ORDER BY time ASC) < temp_celsius THEN -1
        ELSE 0
    END                                                AS trend
FROM sensor_climate
WHERE device_id = 'S-A01'
  AND time >= now() - 30m
ORDER BY time ASC;
```

---

## 6. 多步差分（N 阶差分）

```sql
-- 二阶差分：检测加速度（变化率的变化率）
-- 适用于振动分析、加速度传感器数据验证
SELECT
    time,
    accel_ms2,
    -- 一阶差分（速度）
    accel_ms2 - LAG(accel_ms2, 1, accel_ms2) OVER (ORDER BY time ASC) AS d1,
    -- 二阶差分（加速度的变化率）
    (accel_ms2 - LAG(accel_ms2, 1, accel_ms2) OVER (ORDER BY time ASC))
    - LAG(
        accel_ms2 - LAG(accel_ms2, 1, accel_ms2) OVER (ORDER BY time ASC),
        1, 0
    ) OVER (ORDER BY time ASC)                         AS d2
FROM sensor_vibration
WHERE device_id = 'VIB-01'
  AND axis = 'x'
  AND time >= now() - 10m
ORDER BY time ASC;
```

---

## 7. 常见陷阱

```sql
-- ❌ 忘记 ORDER BY time，LAG 结果不可预测
SELECT time, cpu_pct, LAG(cpu_pct) OVER () AS prev
FROM host_cpu WHERE host = 'server-01';

-- ✅ 必须指定 ORDER BY time ASC
SELECT time, cpu_pct, LAG(cpu_pct) OVER (ORDER BY time ASC) AS prev
FROM host_cpu WHERE host = 'server-01' AND time >= now() - 1h;

-- ❌ 除以时间差时未处理 0（第一行时间差为 0）
SELECT (cpu_pct - LAG(cpu_pct) OVER (ORDER BY time ASC))
       / (time - LAG(time) OVER (ORDER BY time ASC))  -- 第一行除以 0！

-- ✅ 用 NULLIF 防止除零
SELECT (cpu_pct - LAG(cpu_pct) OVER (ORDER BY time ASC))
       / NULLIF(time - LAG(time) OVER (ORDER BY time ASC), 0)
FROM host_cpu WHERE host = 'server-01' AND time >= now() - 1h;

-- ❌ 累计量差分时未过滤重置（计数器重置会产生负值）
-- 如果 energy_kwh 在设备重启后从 0 开始，差分会出现大负值

-- ✅ 过滤负差分值（累计量重置保护）
SELECT time, delta_kwh
FROM (
    SELECT time,
           energy_kwh - LAG(energy_kwh, 1, energy_kwh) OVER (ORDER BY time ASC) AS delta_kwh
    FROM sensor_power WHERE meter_id = 'M-01' AND time >= now() - 1h
) AS t
WHERE delta_kwh >= 0;   -- 过滤负值（计数器重置）
```
