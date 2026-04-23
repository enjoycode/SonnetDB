---
name: forecast-howto
description: 使用 SonnetDB 时序预测函数做短期预测与置信区间估计。
triggers:
  - forecast
  - 预测
  - holt-winters
  - arima
  - 趋势
requires_tools:
  - query_sql
  - describe_measurement
---

# 时序预测快速入门

适用：用户希望对一条时序做短期预测（小时/天），或要算简单的趋势/季节项。

## 推荐函数

- `forecast_ema(series, horizon)` — 指数平滑，适合无明显季节性。
- `forecast_holt_winters(series, horizon, season)` — 适合有日/周季节性。
- `forecast_naive(series, horizon)` — 基线，做对比。

## 模板

```sql
SELECT
  ts AS time,
  yhat,
  yhat_lower,
  yhat_upper
FROM forecast_holt_winters(
  (SELECT time, value FROM kpi_qps WHERE time >= now() - 7d),
  horizon => 24,
  season => 24
);
```

## 步骤

1. `describe_measurement` 确认要预测的列是 `Float64` 或 `Int64`。
2. 取至少 3 个完整季节周期的数据作为历史输入，否则置信区间不可信。
3. 把预测结果写回另一张 measurement，便于在前端对比真实值。
4. 对于业务高峰（promo/活动）需要剔除离群点，或换 `forecast_ets`。

## 反模式

- 数据稀疏 / 大量 NULL → 先 `fill('linear')`。
- horizon 大于历史长度的 1/3 → 误差快速放大，建议拆成多步。
