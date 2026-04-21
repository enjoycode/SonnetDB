# SonnetDB.Benchmarks — 时序数据库性能对比基准

本项目使用 [BenchmarkDotNet](https://benchmarkdotnet.org/) 在相同环境下对比以下四种数据库的
**100 万条数据写入、时间范围查询、1 分钟桶聚合与 Compaction** 的性能：

| 数据库 | 类型 | 说明 |
|--------|------|------|
| **SonnetDB** | 嵌入式时序数据库 | 真实引擎路径，覆盖 WAL、MemTable、Segment、查询、聚合与 Compaction |
| **SQLite** | 嵌入式关系数据库 | 文件模式，WAL 日志，事务批量提交 |
| **InfluxDB 2.x** | 时序数据库 | Line Protocol 写入，Flux 查询 |
| **TDengine 3.x** | 时序数据库 | REST API，超级表模型 |

---

## 快速开始

### 1. 一键运行（推荐）

```bash
dotnet run --project eng/benchmarks/run-benchmarks/run-benchmarks.csproj -- --filter *
```

该入口会先调用 `start-benchmark-env.csproj` 执行 `docker compose build`、`docker compose up -d`，等待 `sonnetdb`、InfluxDB、TDengine 三个容器健康检查通过后，再以 Release 模式运行 BenchmarkDotNet。

常用筛选：

```bash
# 仅运行写入基准
dotnet run --project eng/benchmarks/run-benchmarks/run-benchmarks.csproj -- --filter *Insert*

# 仅运行查询基准
dotnet run --project eng/benchmarks/run-benchmarks/run-benchmarks.csproj -- --filter *Query*

# 仅运行聚合基准
dotnet run --project eng/benchmarks/run-benchmarks/run-benchmarks.csproj -- --filter *Aggregate*

# 仅运行压缩（Compaction）基准
dotnet run --project eng/benchmarks/run-benchmarks/run-benchmarks.csproj -- --filter *Compaction*
```

### 2. 单独管理 Docker 环境

只构建并启动外部数据库/服务端：

```bash
dotnet run --project eng/benchmarks/start-benchmark-env/start-benchmark-env.csproj --
```

重置卷后重新启动：

```bash
dotnet run --project eng/benchmarks/start-benchmark-env/start-benchmark-env.csproj -- --reset-volumes
```

停止并删除卷：

```bash
dotnet run --project eng/benchmarks/start-benchmark-env/start-benchmark-env.csproj -- --down --remove-volumes
```

### 3. 手工运行基准测试

> **注意：** BenchmarkDotNet 必须以 Release 模式运行。

```bash
# 运行所有基准
dotnet run -c Release --project tests/SonnetDB.Benchmarks -- --filter *

# 仅运行写入基准（最耗时，约 3 × 4 次 100 万条写入）
dotnet run -c Release --project tests/SonnetDB.Benchmarks -- --filter *Insert*

# 仅运行查询基准
dotnet run -c Release --project tests/SonnetDB.Benchmarks -- --filter *Query*

# 仅运行聚合基准
dotnet run -c Release --project tests/SonnetDB.Benchmarks -- --filter *Aggregate*

# 仅运行压缩（Compaction）基准
dotnet run -c Release --project tests/SonnetDB.Benchmarks -- --filter *Compaction*
```

### 4. 停止外部数据库

```bash
dotnet run --project eng/benchmarks/start-benchmark-env/start-benchmark-env.csproj -- --down --remove-volumes
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
| `SonnetDB_Insert_1M` | 真实 `Tsdb.WriteMany` + `FlushNow` 路径（MemTable + WAL + Segment） |
| `SQLite_Insert_1M` | 文件数据库 + WAL + 单事务批量 INSERT |
| `InfluxDB_Insert_1M` | WriteApiAsync，10,000 条/批 Line Protocol |
| `TDengine_Insert_1M` | REST API，1,000 条/批 SQL INSERT（显式 STable + 子表） |
| `TDengine_InsertSchemaless_1M` | InfluxDB-compat schemaless 端点 `POST /influxdb/v1/write?precision=ms`，100,000 行/批 LP（PR #49 引入） |

运行策略：Monitoring（0 次预热，3 次迭代）。

### QueryBenchmark（范围查询）

查询最后 10% 的时间段（约 100,000 条）：`ts >= 2024-01-11T09:46:40Z && ts < 2024-01-12T13:46:40Z`

### AggregateBenchmark（聚合）

对全量 100 万条按 **1 分钟桶** 计算 AVG / MIN / MAX / COUNT，
结果约含 **16,667 个桶**。

### CompactionBenchmark（压缩）

预先写入多个 `.SDBSEG` 段，然后执行一次 `4 -> 1` 的段合并，
用于度量 SonnetDB 引擎在真实段文件上的 Compaction 耗时与内存分配。

---

## 外部数据库连接配置

### InfluxDB

| 参数 | 值 |
|------|----|
| URL | `http://localhost:8086` |
| Token | `my-super-secret-auth-token` |
| Org | `sndb` |
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

> 以下数据为 **PR #49** 实测结果（i9-13900HX / Windows 11 / .NET 10.0.6 / Docker Desktop + WSL2，全集 24 个基准 ~20 分钟）。InfluxDB 2.7、TDengine 3.3.4.3、`sonnetdb` 均跑在本机 docker 容器中，仅作同机粗略对比，不代表生产部署性能。

```
// InsertBenchmark（100 万条，IterationCount=3）
| Method                              | Mean        | Allocated  | vs SonnetDB |
|------------------------------------ |------------:|-----------:|----------:|
| SonnetDB_Insert_1M                    |    544.9 ms |  529.74 MB |     1.00× |
| SQLite_Insert_1M                    |    811.4 ms |  465.40 MB |     1.49× |
| InfluxDB_Insert_1M                  |  5,222.3 ms | 1457.45 MB |     9.58× |
| TDengine_Insert_1M (REST INSERT)    | 44,137.4 ms |  156.08 MB |    81.0×  |
| TDengine_InsertSchemaless_1M (新增) |    996.0 ms |   61.22 MB |     1.83× |

// QueryBenchmark（最近 10% 时间窗口范围查询，~100k 条）
| Method               | Mean       | Allocated |
|--------------------- |-----------:|----------:|
| SonnetDB_Query_Range   |   6.71 ms  |  18.69 MB |
| SQLite_Query_Range   |  44.54 ms  |   9.82 MB |
| InfluxDB_Query_Range | 411.13 ms  | 280.52 MB |
| TDengine_Query_Range |  56.29 ms  |  14.00 MB |

// AggregateBenchmark（1 分钟桶 AVG/MIN/MAX/COUNT，16,667 桶）
| Method                  | Mean       | Allocated |
|------------------------ |-----------:|----------:|
| SonnetDB_Aggregate_1Min   |   42.26 ms |  39.41 MB |
| SQLite_Aggregate_1Min   |  327.29 ms |   2.50 MB |
| InfluxDB_Aggregate_1Min |   81.48 ms |  47.24 MB |
| TDengine_Aggregate_1Min |   59.63 ms |   3.08 MB |

// CompactionBenchmark
| Method                       | Mean      | Allocated |
|----------------------------- |----------:|----------:|
| SonnetDB_Compaction_4_to_1     |  16.25 ms |  28.28 MB |

// ServerInsertBenchmark（SonnetDB 同机容器×100 万条）
| Method                                | Mean      | Allocated |
|-------------------------------------- |----------:|----------:|
| SonnetDB Server SQL Batch (/sql/batch)  | 19.797  s | 655.45 MB |
| SonnetDB Server LP    (/measurements/.../lp)   | 1.293 s |  52.36 MB |
| SonnetDB Server JSON  (/measurements/.../json) | 1.352 s |  71.43 MB |
| SonnetDB Server Bulk  (/measurements/.../bulk) | 1.120 s |  34.24 MB |

// ServerQueryBenchmark / ServerAggregateBenchmark
| Method                       | Mean      | Allocated |
|----------------------------- |----------:|----------:|
| SonnetDB Server Query (10%)    |  88.40 ms |  16.07 MB |
| SonnetDB Server Aggregate 1Min |  88.82 ms |   2.47 MB |
```

### PR #49 关键结论

- **SonnetDB 嵌入式写入**：544.9 ms / 1M 点 ≈ **1.83 M pts/s**，比 InfluxDB 快 **9.6×**、比 TDengine REST INSERT 子表路径快 **81×**、比 TDengine schemaless LP 快 **1.8×**。
- **SonnetDB 范围查询**：6.71 ms / 100k 条，比 InfluxDB 快 **61×**、比 SQLite 快 **6.6×**。
- **SonnetDB 聚合**：42 ms / 16,667 桶，比 SQLite 快 **7.7×**。
- **TDengine schemaless LP** （1.00 s / 61 MB）比同库 REST INSERT 子表路径（44 s / 156 MB）快约 **44×**、分配缩到 **39%**，体现 schemaless 快路径与走 SQL parser 路径的差异。
- **SonnetDB LP / JSON / Bulk 三端点** （1.12–1.35 s / 34–71 MB）已进入「秒级 1M 点 + ≤ 80 MB 分配」区间，比 SQL Batch (/sql/batch) 路径快约 **15–7×**、分配缩到 **5–11%**；比嵌入式仅多 **~2.0–2.5×**额外开销（HTTP + Kestrel + Auth + JSON/LP 解析）。
```
