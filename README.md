# TSLite

> 一个使用 C# / .NET 10 编写的嵌入式单文件时序数据库（Time-Series Database）

[![Build](https://img.shields.io/github/actions/workflow/status/maikebing/TSLite/ci.yml?label=Build)](https://github.com/maikebing/TSLite/actions)
[![NuGet](https://img.shields.io/nuget/v/TSLite?label=NuGet)](https://www.nuget.org/packages/TSLite)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10-blueviolet)](https://dotnet.microsoft.com/)

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