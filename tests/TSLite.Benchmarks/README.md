# TSLite.Benchmarks — 时序数据库性能对比基准

本项目使用 [BenchmarkDotNet](https://benchmarkdotnet.org/) 在相同环境下对比以下四种数据库的
**100 万条数据写入、时间范围查询和 1 分钟桶聚合** 的性能：

| 数据库 | 类型 | 说明 |
|--------|------|------|
| **TSLite** | 内存占位 | 当前处于 Milestone 0，尚未实现持久化，结果代表纯内存操作基线 |
| **SQLite** | 嵌入式关系数据库 | 文件模式，WAL 日志，事务批量提交 |
| **InfluxDB 2.x** | 时序数据库 | Line Protocol 写入，Flux 查询 |
| **TDengine 3.x** | 时序数据库 | REST API，超级表模型 |

---

## 快速开始

### 1. 启动外部数据库（Docker）

```bash
docker compose -f tests/TSLite.Benchmarks/docker/docker-compose.yml up -d
```

等待容器健康检查通过后再运行基准（约 10–30 秒）：

```bash
docker compose -f tests/TSLite.Benchmarks/docker/docker-compose.yml ps
```

### 2. 运行基准测试

> **注意：** BenchmarkDotNet 必须以 Release 模式运行。

```bash
# 运行所有基准
dotnet run -c Release --project tests/TSLite.Benchmarks -- --filter *

# 仅运行写入基准（最耗时，约 3 × 4 次 100 万条写入）
dotnet run -c Release --project tests/TSLite.Benchmarks -- --filter *Insert*

# 仅运行查询基准
dotnet run -c Release --project tests/TSLite.Benchmarks -- --filter *Query*

# 仅运行聚合基准
dotnet run -c Release --project tests/TSLite.Benchmarks -- --filter *Aggregate*
```

### 3. 停止外部数据库

```bash
docker compose -f tests/TSLite.Benchmarks/docker/docker-compose.yml down -v
```

---

## 基准说明

### 数据集

- 数据量：**1,000,000 条**
- 时间范围：2024-01-01 00:00:00 UTC — 2024-01-12 13:46:39 UTC（每秒 1 条）
- 序列键：`measurement=sensor_data, host=server001`
- 字段：`value=double`（随机值，范围 0–100，固定随机种子 42）

### InsertBenchmark（写入）

| 方法 | 说明 |
|------|------|
| `TSLite_Insert_1M` | 向 `List<T>` 追加（内存基线） |
| `SQLite_Insert_1M` | 文件数据库 + WAL + 单事务批量 INSERT |
| `InfluxDB_Insert_1M` | WriteApiAsync，10,000 条/批 Line Protocol |
| `TDengine_Insert_1M` | REST API，1,000 条/批 SQL INSERT |

运行策略：Monitoring（0 次预热，3 次迭代）。

### QueryBenchmark（范围查询）

查询最后 10% 的时间段（约 100,000 条）：`ts >= 2024-01-11T09:46:40Z && ts < 2024-01-12T13:46:40Z`

### AggregateBenchmark（聚合）

对全量 100 万条按 **1 分钟桶** 计算 AVG / MIN / MAX / COUNT，
结果约含 **16,667 个桶**。

---

## 外部数据库连接配置

### InfluxDB

| 参数 | 值 |
|------|----|
| URL | `http://localhost:8086` |
| Token | `my-super-secret-auth-token` |
| Org | `tslite` |
| Bucket | `benchmarks` |

### TDengine

| 参数 | 值 |
|------|----|
| REST URL | `http://localhost:6041` |
| Username | `root` |
| Password | `taosdata` |

如需修改连接参数，请编辑各 `*Benchmark.cs` 文件顶部的常量。

---

## 预期结果样例

> 以下数据仅为示意，实际结果因硬件和网络环境而异。

```
// InsertBenchmark
| Method               | Mean      | Allocated |
|--------------------- |----------:|----------:|
| TSLite_Insert_1M     |   80 ms   |  ~40 MB   |
| SQLite_Insert_1M     |  500 ms   |  ~10 MB   |
| InfluxDB_Insert_1M   | 8,000 ms  |  ~80 MB   |
| TDengine_Insert_1M   | 6,000 ms  |  ~20 MB   |

// QueryBenchmark
| Method               | Mean      | Allocated |
|--------------------- |----------:|----------:|
| TSLite_Query_Range   |  10 ms    |  ~8 MB    |
| SQLite_Query_Range   |  30 ms    |  ~8 MB    |
| InfluxDB_Query_Range | 200 ms    |  ~12 MB   |
| TDengine_Query_Range | 100 ms    |  ~0.5 MB  |
```
