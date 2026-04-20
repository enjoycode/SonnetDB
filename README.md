# TSLite

> 一个使用 C# / .NET 10 编写的嵌入式单文件时序数据库（Time-Series Database）

[![CI](https://github.com/maikebing/TSLite/actions/workflows/ci.yml/badge.svg)](https://github.com/maikebing/TSLite/actions/workflows/ci.yml)
[![CodeQL](https://github.com/maikebing/TSLite/actions/workflows/codeql.yml/badge.svg)](https://github.com/maikebing/TSLite/actions/workflows/codeql.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)

---

## 简介

**TSLite** 定位类似 SQLite，但面向时序数据场景：

- **嵌入式**：进程内运行，零外部依赖
- **单文件持久化**：数据库以单个 `.tsl` 文件存储
- **SQL 接口**：通过 SQL 语句进行 `CREATE / INSERT / SELECT` 操作
- **高性能**：基于 `Span<T>` / `MemoryMarshal` / `BinaryPrimitives`，Safe-only，无 `unsafe`
- **时序优化**：WAL + MemTable + Segment，Append-only 写入，时间戳 delta 压缩
- **Tag / Field 模型**：参考 InfluxDB 行协议，支持多维标签过滤与字段聚合
- **InlineArray 固定缓冲**：利用 `[InlineArray(N)]` 处理 magic bytes 和保留字段
- **结构化二进制格式**：`[StructLayout(LayoutKind.Sequential, Pack = 1)]` 的 `unmanaged struct`

---

## 特性 (Features)

| 特性 | 说明 |
|------|------|
| 嵌入式 | 类库形式引用，进程内运行 |
| 单文件 | 单 `.tsl` 文件包含所有数据 |
| 零依赖 | 不依赖任何第三方运行时库 |
| SQL 接口 | 支持 `CREATE MEASUREMENT / INSERT INTO / SELECT ... GROUP BY time(...)` |
| Safe-only | 第一版不使用 `unsafe`，全部通过 `Span<T>` / `MemoryMarshal` 操作底层内存 |
| 高性能写入 | WAL + MemTable + Immutable Segment + Compaction |
| 标签索引 | measurement + tags 组合构成 series key，支持按标签过滤 |
| 时间聚合 | `min / max / sum / avg / count` + `GROUP BY time(10s)` 时间桶聚合 |
| 压缩 | 时间戳 delta 编码、值列 delta/XOR 编码 |
| 并发模型 | 单写多读（第一版） |
| 服务器模式 | `TSLite.Server` AOT Minimal API + 内嵌 Vue3 管理后台 + 远端 ADO.NET |
| 实时事件流 | `GET /v1/events` SSE 推送指标 / 慢查询 / 数据库事件，前端订阅自动刷新 |

---

## 快速开始 (Quick Start)

> **注意：以下为目标 API，尚未实现。**

```csharp
using TSLite;

using var db = new TsdbDatabase("metrics.tsl");

db.Execute("""
    CREATE MEASUREMENT cpu (
        TAG host,
        TAG region,
        FIELD usage DOUBLE,
        FIELD system DOUBLE
    )
""");

db.Execute("""
    INSERT INTO cpu(host, region, usage, system, time)
    VALUES ('server-1', 'us-east', 63.2, 12.1, 1776477601000)
""");

using var reader = db.Query("""
    SELECT time, mean(usage)
    FROM cpu
    WHERE host = 'server-1' AND time >= 1776477600000
    GROUP BY time(10s)
""");

while (reader.Read())
{
    Console.WriteLine($"{reader.GetInt64(0)}  {reader.GetDouble(1)}");
}
```

---

## 发布与安装

`TSLite 0.1.0` 现已定义完整发布矩阵，包括：

- NuGet：`TSLite`、`TSLite.Data`、`TSLite.Cli`
- SDK Bundle：Windows / Linux 的开发者打包版本
- Server Full Bundle：包含服务端、前端、CLI、预置数据目录与默认本地凭据的一键启动包
- Installer：Windows `msi`、Linux `deb` / `rpm`

详细说明见：

- [docs/releases/README.md](docs/releases/README.md)
- [docs/releases/sdk-bundle.md](docs/releases/sdk-bundle.md)
- [docs/releases/server-bundle.md](docs/releases/server-bundle.md)
- [docs/releases/installers.md](docs/releases/installers.md)

---

## 数据模型

| 概念 | 类比 | 说明 |
|------|------|------|
| `measurement` | 表名 | 指标/度量名称，如 `cpu`、`temperature` |
| `tag` | 有索引的维度列 | 离散维度，用于过滤与分组，如 `host=server-1` |
| `field` | 值列 | 随时间变化的数值，如 `usage=63.2` |
| `timestamp` | 主键（时间维度） | Unix 毫秒时间戳，序列内单调递增 |
| `series` | 逻辑时间序列 | `series_key = measurement + sorted(tags)`，唯一标识一条时间序列 |

### Series 示例

```
cpu{host=server-1, region=us-east}
  ├── (1776477601000, usage=63.2, system=12.1)
  ├── (1776477602000, usage=61.8, system=11.9)
  └── (1776477603000, usage=65.0, system=13.0)

cpu{host=server-2, region=us-east}
  └── (1776477601000, usage=72.1, system=18.3)
```

---

## 架构总览

```
┌──────────────────────────────────────────┐
│               SQL API                    │
│   TsdbDatabase / TsdbConnection          │
│   TsdbCommand / TsdbDataReader           │
└──────────────┬───────────────────────────┘
               ↓
┌──────────────────────────────────────────┐
│           Query Engine                   │
│   SQL Parser (递归下降)                   │
│   QueryPlanner ── Aggregator             │
│   (min/max/sum/avg/count + time bucket)  │
└──────────────┬───────────────────────────┘
               ↓
┌──────────────────────────────────────────┐
│          Storage Engine                  │
│  ┌─────────────────────────────────────┐ │
│  │  WAL  (预写日志，append-only)        │ │
│  ├─────────────────────────────────────┤ │
│  │  MemTable  (内存缓冲)                │ │
│  ├─────────────────────────────────────┤ │
│  │  Segment Files                       │ │
│  │    BlockHeader + Payload + Footer    │ │
│  ├─────────────────────────────────────┤ │
│  │  Series Catalog  (series_key 映射)   │ │
│  └─────────────────────────────────────┘ │
└──────────────────────────────────────────┘
```

---

## 技术栈

| 组件 | 技术 |
|------|------|
| 语言 | C# (latest) |
| 运行时 | .NET 10 |
| 项目类型 | 类库（`Microsoft.NET.Sdk`，`net10.0`） |
| 单元测试 | xUnit |
| 基准测试 | BenchmarkDotNet |
| CI | GitHub Actions（ubuntu-latest / windows-latest） |
| 代码风格 | `.editorconfig` + `Nullable enable` + `ImplicitUsings enable` + `TreatWarningsAsErrors` |
| License | MIT |

---

## 设计原则

### Safe-only 优先
第一版**不使用 `unsafe`**，所有底层内存操作通过：
- `Span<T>` / `ReadOnlySpan<T>` / `Memory<T>`
- `MemoryMarshal`（`CreateSpan`、`Read`、`Write`、`AsBytes`、`Cast`）
- `BinaryPrimitives`
- `[InlineArray(N)]`（magic bytes、固定保留字段、小型 scratch buffer）
- `ArrayPool<T>`、`stackalloc`、`CollectionsMarshal`

### Span-first
所有内部序列化/反序列化接口以 `Span<byte>` / `ReadOnlySpan<byte>` 为参数，避免不必要的内存分配。

### Struct-based Binary
固定 header / index entry 使用 `[StructLayout(LayoutKind.Sequential, Pack = 1)]` 的 `unmanaged struct`，字节序统一 **little-endian**。

### Append-only + LSM 风格
写入路径：`WAL → MemTable → Flush → Immutable Segment → Compaction`，不覆盖原始点。

### 单写多读（第一版）
第一版并发模型为单写多读，后续版本扩展为多写并发。

---

## 目录结构（规划）

```
TSLite/
├── src/
│   ├── TSLite/                    # 核心类库
│   └── TSLite.Cli/                # 命令行工具
├── tests/
│   ├── TSLite.Tests/              # xUnit 单元测试
│   └── TSLite.Benchmarks/         # BenchmarkDotNet 基准测试
├── docs/                          # 额外文档
├── .github/
│   └── workflows/
│       └── ci.yml                 # GitHub Actions CI
├── .editorconfig
├── Directory.Build.props
├── TSLite.sln
├── README.md
├── CHANGELOG.md
├── ROADMAP.md
├── AGENTS.md
└── LICENSE
```

---

## 性能基准 (Benchmarks)

下表为在同一台开发机上对 **TSLite（嵌入式）/ SQLite / InfluxDB 2.x / TDengine 3.x / TSLite.Server（HTTP）** 五种时序/嵌入式数据库的实测结果，本轮所有测项均在同一台 Windows 主机上运行（外部数据库与 TSLite.Server 均为 Docker 容器，loopback 访问）。基准代码位于 [tests/TSLite.Benchmarks](tests/TSLite.Benchmarks)，使用 [BenchmarkDotNet](https://benchmarkdotnet.org/) 运行。

### 五库基准覆盖一览

| 数据库 | 写入 | 范围查询 | 时间桶聚合 | Compaction |
|--------|:---:|:------:|:---------:|:---------:|
| **TSLite**（嵌入式） | ✅ | ✅ | ✅ | ✅ |
| **SQLite** | ✅ | ✅ | ✅ | — |
| **InfluxDB 2.7** | ✅ | ✅ | ✅ | — |
| **TDengine 3.3** | ✅ | ✅ | ✅ | — |
| **TSLite.Server**（HTTP） | ✅ | ✅ | ✅ | — |

> Compaction 是 TSLite 段合并机制特有概念，其它数据库无对应可对比的对外接口；本基准仅记录 TSLite 单库的 4→1 段合并耗时。
> 五库的 Insert / Query / Aggregate 基准实现在 [tests/TSLite.Benchmarks/Benchmarks/](tests/TSLite.Benchmarks/Benchmarks/) 下；外部数据库经 Docker compose 一键启动（见 [tests/TSLite.Benchmarks/docker/docker-compose.yml](tests/TSLite.Benchmarks/docker/docker-compose.yml)），不可用时对应基准自动 `[SKIP]`。

### 测试环境

| 项目 | 配置 |
|------|------|
| CPU | 13th Gen Intel Core i9-13900HX @ 2.20 GHz（24 物理核 / 32 逻辑核） |
| 操作系统 | Windows 11 26200.8246 (25H2) x64 |
| .NET SDK | 10.0.202 |
| 运行时 | .NET 10.0.6, X64 RyuJIT x86-64-v3 |
| BenchmarkDotNet | v0.15.8（含 `MemoryDiagnoser`） |
| TSLite | 当前主分支（参见 [CHANGELOG.md](CHANGELOG.md)） |
| SQLite | `Microsoft.Data.Sqlite`（`journal_mode=WAL`、`synchronous=OFF`） |
| InfluxDB | `influxdb:2.7` 容器，HTTP API（10k/批，毫秒精度） |
| TDengine | `tdengine/tdengine:3.3.4.3` 容器，REST API（1k/批，超级表 + 子表） |
| TSLite.Server | `iotsharp/tslite-server:bench` 容器（源码构建，Release，Framework-dependent） |
| 容器运行时 | Docker Desktop（loopback `localhost` 访问） |

### 工作负载

- 数据规模：**1,000,000 个数据点**
- Measurement：`sensor_data`，Tag：`host=server001`，Field：`value DOUBLE`
- 时间戳：`2024-01-01T00:00:00Z` 起，每点 +1 ms（嵌入式）/ +1 s（TSLite.Server，使用 INSERT SQL 语法）
- 写入基准每次迭代均使用全新的数据库 / bucket / 子表；查询/聚合基准在 `[GlobalSetup]` 中预先写入 1M 点
- TSLite.Server 写入采用 HTTP Batch API（每批 2,000 条 INSERT SQL），范围查询取最后 10% 时间段（约 100,000 行 ndjson）

### 写入：100 万点（单序列）

| 实现 | Mean | StdDev | 内存分配（客户端） | 相对 TSLite |
|------|-----:|-------:|------------------:|-----------:|
| **TSLite**（嵌入式 baseline） | **620.9 ms** | 320.2 ms | 529.74 MB | **1.00×** |
| SQLite | 758.7 ms | 158.6 ms | 465.40 MB | 1.22× 慢 |
| InfluxDB 2.7（HTTP） | 4,674.0 ms | 220.5 ms | 1,457.44 MB | 7.53× 慢 |
| TDengine 3.3（REST） | 44,291.1 ms | 117.8 ms | 156.06 MB | 71.3× 慢 |
| TSLite.Server（HTTP Batch 2k/批） | 20,974 ms | 608 ms | 654.65 MB | 33.8× 慢 |

> TSLite 嵌入式写入吞吐 ≈ **1.61 M points/s**（单序列、`FlushPolicy` 全部上限、后台 flush 与压缩关闭）。
> InfluxDB / TDengine / TSLite.Server 都走 HTTP，受网络与协议序列化开销影响；本基准仅供水平参考，不代表生产部署吞吐上限。

### 范围查询：扫描最后 10% 数据（约 10 万点）

| 实现 | Mean | StdDev | 内存分配 | 相对 TSLite |
|------|-----:|-------:|---------:|-----------:|
| **TSLite**（嵌入式 baseline） | **9.02 ms** | 2.46 ms | 18.69 MB | **1.00×** |
| SQLite | 66.89 ms | 1.57 ms | 9.82 MB | 7.42× 慢 |
| InfluxDB 2.7（Flux） | 674.31 ms | 12.99 ms | 280.52 MB | 74.8× 慢 |
| TDengine 3.3（REST） | 62.41 ms | 2.23 ms | 13.99 MB | 6.92× 慢 |
| TSLite.Server（HTTP + ndjson） | 92.83 ms | 8.03 ms | 16.06 MB | 10.3× 慢 |

### 时间桶聚合：`avg / 1 minute` over 1M 点

| 实现 | Mean | StdDev | 内存分配 | 相对 TSLite |
|------|-----:|-------:|---------:|-----------:|
| **TSLite**（嵌入式 baseline） | **40.38 ms** | 1.41 ms | 39.41 MB | **1.00×** |
| SQLite | 312.96 ms | 13.73 ms | 2.50 MB | 7.75× 慢 |
| InfluxDB 2.7（Flux） | 92.56 ms | 22.08 ms | 47.24 MB | 2.29× 慢 |
| TDengine 3.3（REST） | 59.87 ms | 3.72 ms | 3.08 MB | 1.48× 慢 |
| TSLite.Server（HTTP，~16,667 桶） | 79.89 ms | 1.42 ms | 2.46 MB | 1.98× 慢 |

### Compaction（仅 TSLite）

| 基准 | Mean | StdDev | 内存分配 |
|------|-----:|-------:|---------:|
| **TSLite Compaction (4→1)**（每段 50,000 点） | **18.40 ms** | 3.53 ms | 28.28 MB |

### 嵌入式 vs. TSLite.Server 同机对比

| 操作 | 嵌入式模式 | TSLite.Server（HTTP） | 额外开销 | 主要来源 |
|------|----------:|----------------------:|---------:|----------|
| 写入 100 万条 | 620.9 ms | 20,974 ms | ~33.8× | 500 次 HTTP 往返 + INSERT SQL 解析 |
| 范围查询（10 万行） | 9.02 ms | 92.83 ms | ~10.3× | ndjson 序列化 + 流式网络传输 |
| 1 分钟桶聚合（~16,667 行） | 40.38 ms | 79.89 ms | ~1.98× | 服务端聚合后小结果集，开销最低 |

### 小结

在本机配置下：

- **嵌入式写入吞吐**：TSLite ~1.22× 优于 SQLite，~7.5× 优于 InfluxDB，~71× 优于 TDengine（REST 批量写入）。
- **范围查询**：TSLite ~7.4× 优于 SQLite 与 TDengine，~75× 优于 InfluxDB（按时间排序的 Segment 直读 vs Flux/B-Tree）。
- **1 分钟桶聚合**：TSLite ~7.8× 优于 SQLite，~2.3× 优于 InfluxDB，~1.5× 优于 TDengine（按 series 内序解码后流式聚合）。
- **TSLite.Server vs 嵌入式**：聚合场景额外开销仅 ~2×，查询 ~10×，写入 ~34×（细粒度 INSERT SQL 是瓶颈，未来可改用 Line Protocol / 二进制批量协议显著降低）。

> InfluxDB / TDengine / TSLite.Server 全部为单机容器 + 客户端跨进程 HTTP 调用；TSLite 嵌入式是 in-process 引擎。
> 这是**嵌入式 vs 服务化**的架构差异，结果用于水平参考，不能直接映射到独立部署的生产集群。

### 启动外部数据库 + TSLite.Server

```bash
cd tests/TSLite.Benchmarks/docker
docker compose up -d        # 启动 tslite-server + influxdb:2.7 + tdengine:3.3.x
cd ../../..
dotnet run -c Release --project tests/TSLite.Benchmarks -- --filter "*"
```

容器默认配置：

| 服务 | URL | 凭据 |
|------|-----|------|
| TSLite.Server | `http://localhost:5080` | Admin Token: `bench-admin-token` |
| InfluxDB | `http://localhost:8086` | Token `my-super-secret-auth-token`, Org `tslite`, Bucket `benchmarks` |
| TDengine | `http://localhost:6041` | `root` / `taosdata` |

如果对应服务不可用，相关测项会自动 `[SKIP]`，仅运行 TSLite + SQLite 对比。

### 复现命令

```bash
# 全部基准（含 Compaction + 服务器模式）
dotnet run -c Release --project tests/TSLite.Benchmarks -- --filter "*"

# 仅写入基准
dotnet run -c Release --project tests/TSLite.Benchmarks -- --filter "*Insert*"

# 仅 TSLite.Server 基准
dotnet run -c Release --project tests/TSLite.Benchmarks -- --filter "*Server*"
```

完整报告（CSV / HTML / Markdown / JSON）会输出到 `BenchmarkDotNet.Artifacts/results/`。

> 注：基准结果与硬件、电源策略、后台负载强相关，请以你自己机器上的复现结果为准。

---

## 路线图

详见 [ROADMAP.md](ROADMAP.md)。

---

## 变更日志

详见 [CHANGELOG.md](CHANGELOG.md)。

---

## Agent 协作规范

AI 辅助开发约定详见 [AGENTS.md](AGENTS.md)。

---

## License

本项目采用 [MIT](LICENSE) 协议开源。
