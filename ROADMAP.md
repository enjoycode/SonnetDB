# ROADMAP

本文件描述 TSLite 的分批 PR 开发计划，按 Milestone 组织。每个 PR 均包含：变更点、新增文件、测试覆盖与验收标准。

---

## Milestone 0 — 项目脚手架

### PR #1（本 PR）：初始化规划文档

| 项目 | 内容 |
|------|------|
| 变更点 | 新增/覆盖 `README.md`、`CHANGELOG.md`、`ROADMAP.md`、`AGENTS.md` |
| 新增文件 | `README.md`、`CHANGELOG.md`、`ROADMAP.md`、`AGENTS.md` |
| 测试覆盖 | 无（文档 PR） |
| 验收标准 | 4 个文件全部存在，Markdown 渲染无误，内容与设计原则一致 |

---

### PR #2：解决方案与项目骨架

| 项目 | 内容 |
|------|------|
| 变更点 | 创建完整解决方案结构与项目文件 |
| 新增文件 | `TSLite.sln`<br>`src/TSLite/TSLite.csproj`（`net10.0`，类库）<br>`src/TSLite.Cli/TSLite.Cli.csproj`（控制台）<br>`tests/TSLite.Tests/TSLite.Tests.csproj`（xUnit）<br>`tests/TSLite.Benchmarks/TSLite.Benchmarks.csproj`（BenchmarkDotNet）<br>`.editorconfig`<br>`Directory.Build.props`（统一 `LangVersion` / `Nullable` / `TreatWarningsAsErrors`） |
| 测试覆盖 | `dotnet build` 全部项目通过，`dotnet test` 0 失败 |
| 验收标准 | `dotnet build TSLite.sln` 成功；`Nullable enable`、`ImplicitUsings enable`、`TreatWarningsAsErrors` 均已配置 |

---

### PR #3：CI（GitHub Actions）

| 项目 | 内容 |
|------|------|
| 变更点 | 新增 CI 工作流 |
| 新增文件 | `.github/workflows/ci.yml` |
| 测试覆盖 | CI 矩阵：ubuntu-latest / windows-latest，步骤：build + test |
| 验收标准 | Push 后 CI 绿色；矩阵两个 job 均通过 |

---

## Milestone 1 — 内存与二进制基础设施（Safe-only）

### PR #4：SpanReader / SpanWriter

| 项目 | 内容 |
|------|------|
| 变更点 | 新增 `ref struct` 读写工具，基于 `BinaryPrimitives` + `MemoryMarshal` |
| 新增文件 | `src/TSLite/IO/SpanReader.cs`<br>`src/TSLite/IO/SpanWriter.cs`<br>`tests/TSLite.Tests/IO/SpanReaderWriterTests.cs` |
| 测试覆盖 | round-trip 测试：`byte/short/int/long/float/double/string`，边界条件（空 span、越界） |
| 验收标准 | 所有测试通过；无 `unsafe`；API 有 XML 文档注释 |

---

### PR #5：[InlineArray] 工具

| 项目 | 内容 |
|------|------|
| 变更点 | 定义 `Magic8`、`Reserved16` 等固定缓冲类型，演示 `MemoryMarshal.CreateSpan` 用法 |
| 新增文件 | `src/TSLite/Buffers/Magic8.cs`<br>`src/TSLite/Buffers/Reserved16.cs`<br>`tests/TSLite.Tests/Buffers/InlineArrayTests.cs` |
| 测试覆盖 | 魔数写入/读取，`CreateSpan` 转换 round-trip |
| 验收标准 | 所有测试通过；类型标注 `[InlineArray(N)]` |

---

### PR #6：核心 unmanaged struct

| 项目 | 内容 |
|------|------|
| 变更点 | 定义所有固定二进制结构体 |
| 新增文件 | `src/TSLite/Format/FileHeader.cs`<br>`src/TSLite/Format/SegmentHeader.cs`<br>`src/TSLite/Format/BlockHeader.cs`<br>`src/TSLite/Format/BlockIndexEntry.cs`<br>`src/TSLite/Format/SegmentFooter.cs`<br>`tests/TSLite.Tests/Format/StructRoundTripTests.cs` |
| 测试覆盖 | 每个结构体的 `MemoryMarshal.AsBytes` round-trip；`sizeof` 断言 |
| 验收标准 | 全部测试通过；每个结构体标注 `[StructLayout(LayoutKind.Sequential, Pack = 1)]`；字节序 little-endian |

---

## Milestone 2 — 逻辑模型与目录

### PR #7：核心数据模型

| 项目 | 内容 |
|------|------|
| 变更点 | 定义时序数据的领域模型 |
| 新增文件 | `src/TSLite/Model/Point.cs`<br>`src/TSLite/Model/DataPoint.cs`<br>`src/TSLite/Model/SeriesFieldKey.cs`<br>`src/TSLite/Model/AggregateResult.cs`<br>`tests/TSLite.Tests/Model/ModelTests.cs` |
| 测试覆盖 | 模型构造、相等性、序列化 |
| 验收标准 | 所有测试通过；public API 有 XML 文档注释 |

---

### PR #8：SeriesKey 规范化与 SeriesId

| 项目 | 内容 |
|------|------|
| 变更点 | 实现 `series_key = measurement + sorted(tags)` 规范化，`SeriesId = XxHash64(series_key)` |
| 新增文件 | `src/TSLite/Model/SeriesKey.cs`<br>`src/TSLite/Model/SeriesId.cs`<br>`tests/TSLite.Tests/Model/SeriesKeyTests.cs` |
| 测试覆盖 | 标签乱序后 key 相同；不同标签组合 id 不冲突；边界（空标签） |
| 验收标准 | 所有测试通过 |

---

### PR #9：SeriesCatalog

| 项目 | 内容 |
|------|------|
| 变更点 | 实现 series 目录（内存 + 持久化 catalog 文件） |
| 新增文件 | `src/TSLite/Catalog/SeriesCatalog.cs`<br>`tests/TSLite.Tests/Catalog/SeriesCatalogTests.cs` |
| 测试覆盖 | 注册/查找 series；持久化后重新加载；并发只读 |
| 验收标准 | 所有测试通过；重启后 catalog 可完整恢复 |

---

## Milestone 3 — 写入路径

### PR #10：WalWriter / WalReader

| 项目 | 内容 |
|------|------|
| 变更点 | 实现 append-only WAL 写入与重放 |
| 新增文件 | `src/TSLite/Wal/WalWriter.cs`<br>`src/TSLite/Wal/WalReader.cs`<br>`src/TSLite/Wal/WalEntry.cs`<br>`tests/TSLite.Tests/Wal/WalTests.cs` |
| 测试覆盖 | 写入多条后重放；模拟崩溃截断后重放；幂等性 |
| 验收标准 | 所有测试通过；WAL 格式有 CRC 校验 |

---

### PR #11：MemTable

| 项目 | 内容 |
|------|------|
| 变更点 | 实现内存写缓冲区 |
| 新增文件 | `src/TSLite/Storage/MemTable.cs`<br>`tests/TSLite.Tests/Storage/MemTableTests.cs` |
| 测试覆盖 | 写入/按 series+time range 查询；超出容量触发 flush 通知 |
| 验收标准 | 所有测试通过；`ConcurrentDictionary<SeriesFieldKey, List<DataPoint>>` 线程安全读写 |

---

### PR #12：SegmentWriter

| 项目 | 内容 |
|------|------|
| 变更点 | 实现不可变 segment 文件写入（BlockHeader + payload + footer index） |
| 新增文件 | `src/TSLite/Storage/SegmentWriter.cs`<br>`tests/TSLite.Tests/Storage/SegmentWriterTests.cs` |
| 测试覆盖 | 写入多 series 多 block，验证 footer index 正确性 |
| 验收标准 | 所有测试通过；输出文件通过二进制 round-trip 验证 |

---

### PR #13：Flush 流程

| 项目 | 内容 |
|------|------|
| 变更点 | 串联 Flush 流程：MemTable → Segment，WAL truncate |
| 新增文件 | `src/TSLite/Storage/FlushManager.cs`<br>`tests/TSLite.Tests/Storage/FlushTests.cs` |
| 测试覆盖 | 写入后触发 flush，验证 segment 文件产生，WAL 截断 |
| 验收标准 | 所有测试通过；数据可从 segment 完整读回 |

---

## Milestone 4 — 查询路径

### PR #14：SegmentReader

| 项目 | 内容 |
|------|------|
| 变更点 | 实现 segment 文件读取，按 seriesId / time range 裁剪 block |
| 新增文件 | `src/TSLite/Storage/SegmentReader.cs`<br>`tests/TSLite.Tests/Storage/SegmentReaderTests.cs` |
| 测试覆盖 | 按时间范围精确裁剪；多 series 独立读取 |
| 验收标准 | 所有测试通过；不读取无关 block |

---

### PR #15：QueryEngine.QueryRaw

| 项目 | 内容 |
|------|------|
| 变更点 | 合并 MemTable + 多 Segment 的原始点查询 |
| 新增文件 | `src/TSLite/Query/QueryEngine.cs`<br>`tests/TSLite.Tests/Query/QueryEngineTests.cs` |
| 测试覆盖 | MemTable + Segment 混合数据按时间顺序合并；重复时间戳保留原始数据 |
| 验收标准 | 所有测试通过 |

---

### PR #16：聚合引擎

| 项目 | 内容 |
|------|------|
| 变更点 | 实现 `min/max/sum/avg/count` + 时间桶 `time(10s)` 分组 |
| 新增文件 | `src/TSLite/Query/Aggregator.cs`<br>`src/TSLite/Query/TimeBucket.cs`<br>`tests/TSLite.Tests/Query/AggregatorTests.cs` |
| 测试覆盖 | 各聚合函数正确性；时间桶边界对齐；空结果集 |
| 验收标准 | 所有测试通过 |

---

## Milestone 5 — SQL 前端

### PR #17：SQL 词法与语法分析器

| 项目 | 内容 |
|------|------|
| 变更点 | 手写递归下降解析器，支持 TSLite SQL 子集 |
| 新增文件 | `src/TSLite/Sql/Lexer.cs`<br>`src/TSLite/Sql/Parser.cs`<br>`src/TSLite/Sql/Ast/`（AST 节点）<br>`tests/TSLite.Tests/Sql/LexerTests.cs`<br>`tests/TSLite.Tests/Sql/ParserTests.cs` |
| 测试覆盖 | 各 token 类型正确识别；AST 节点结构验证；错误输入的异常处理 |
| 验收标准 | 所有测试通过；不依赖第三方解析库 |

---

### PR #18：CREATE MEASUREMENT 语句

| 项目 | 内容 |
|------|------|
| 变更点 | 实现 `CREATE MEASUREMENT` 语句的解析与执行 |
| 新增文件 | `src/TSLite/Sql/Execution/CreateMeasurementExecutor.cs`<br>`tests/TSLite.Tests/Sql/CreateMeasurementTests.cs` |
| 测试覆盖 | 建表成功；重复建表处理；schema 持久化后重启可读 |
| 验收标准 | 所有测试通过 |

---

### PR #19：INSERT INTO ... VALUES 语句

| 项目 | 内容 |
|------|------|
| 变更点 | 实现 `INSERT INTO ... VALUES (...)` 语句 |
| 新增文件 | `src/TSLite/Sql/Execution/InsertExecutor.cs`<br>`tests/TSLite.Tests/Sql/InsertTests.cs` |
| 测试覆盖 | 单条/批量插入；类型校验（TAG/FIELD）；时间戳缺省处理 |
| 验收标准 | 所有测试通过 |

---

### PR #20：SELECT ... WHERE ... GROUP BY time(...)

| 项目 | 内容 |
|------|------|
| 变更点 | 实现完整 SELECT 查询语句 |
| 新增文件 | `src/TSLite/Sql/Execution/SelectExecutor.cs`<br>`tests/TSLite.Tests/Sql/SelectTests.cs` |
| 测试覆盖 | 全量查询；时间范围过滤；tag 过滤；聚合 + GROUP BY time |
| 验收标准 | 所有测试通过 |

---

### PR #21：ADO.NET 风格 API

| 项目 | 内容 |
|------|------|
| 变更点 | 提供 `TsdbConnection / TsdbCommand / TsdbDataReader` API |
| 新增文件 | `src/TSLite/Api/TsdbDatabase.cs`<br>`src/TSLite/Api/TsdbConnection.cs`<br>`src/TSLite/Api/TsdbCommand.cs`<br>`src/TSLite/Api/TsdbDataReader.cs`<br>`tests/TSLite.Tests/Api/ApiIntegrationTests.cs` |
| 测试覆盖 | README 中目标 API 示例代码可运行；`using` 资源释放；异常安全 |
| 验收标准 | 集成测试通过；API 与 README 示例一致 |

---

## Milestone 6 — 压缩与 Compaction

### PR #22：时间戳 delta 编码

| 项目 | 内容 |
|------|------|
| 变更点 | block payload V2：时间戳 delta 编码 |
| 新增文件 | `src/TSLite/Compression/DeltaTimestampEncoder.cs`<br>`tests/TSLite.Tests/Compression/DeltaTimestampTests.cs` |
| 测试覆盖 | 编码/解码 round-trip；乱序数据处理；单点边界 |
| 验收标准 | 所有测试通过；压缩率优于原始存储 |

---

### PR #23：值列 delta 编码

| 项目 | 内容 |
|------|------|
| 变更点 | 值列 delta/XOR 编码 |
| 新增文件 | `src/TSLite/Compression/DeltaValueEncoder.cs`<br>`tests/TSLite.Tests/Compression/DeltaValueTests.cs` |
| 测试覆盖 | double/long 类型 round-trip；小幅波动数据压缩率验证 |
| 验收标准 | 所有测试通过 |

---

### PR #24：CompactionEngine

| 项目 | 内容 |
|------|------|
| 变更点 | 合并旧 segment，减少文件数量 |
| 新增文件 | `src/TSLite/Storage/CompactionEngine.cs`<br>`tests/TSLite.Tests/Storage/CompactionTests.cs` |
| 测试覆盖 | 多 segment 合并后数据完整；compaction 后查询结果不变 |
| 验收标准 | 所有测试通过；compaction 前后数据一致 |

---

## Milestone 7 — 单文件容器（演进）

### PR #25：page manager + free list

| 项目 | 内容 |
|------|------|
| 变更点 | 实现页管理与空闲链表 |
| 新增文件 | `src/TSLite/PageStore/PageManager.cs`<br>`src/TSLite/PageStore/FreeList.cs`<br>`tests/TSLite.Tests/PageStore/PageManagerTests.cs` |
| 测试覆盖 | 分配/释放/重用；崩溃恢复 |
| 验收标准 | 所有测试通过 |

---

### PR #26：单文件容器合并

| 项目 | 内容 |
|------|------|
| 变更点 | 将 manifest / wal / segments 合并进单一 `.tsl` 文件 |
| 新增文件 | `src/TSLite/PageStore/ContainerManager.cs`<br>`tests/TSLite.Tests/PageStore/ContainerTests.cs` |
| 测试覆盖 | 完整写入→查询→重启→查询流程；文件格式向后兼容 |
| 验收标准 | 所有测试通过；`FileHeader.Version` 升级 |

---

## Milestone 8 — 性能与发布

### PR #27：BenchmarkDotNet 基准

| 项目 | 内容 |
|------|------|
| 变更点 | 补充写入/查询/聚合基准 |
| 新增文件 | `tests/TSLite.Benchmarks/WriteBenchmarks.cs`<br>`tests/TSLite.Benchmarks/QueryBenchmarks.cs`<br>`tests/TSLite.Benchmarks/AggregateBenchmarks.cs` |
| 测试覆盖 | 基准可正常运行（`--filter *`） |
| 验收标准 | 基准结果记录到 PR 描述 |

---

### PR #28：文档完善与使用示例

| 项目 | 内容 |
|------|------|
| 变更点 | 更新 README、补充 `docs/` 目录文档 |
| 新增文件 | `docs/getting-started.md`<br>`docs/data-model.md`<br>`docs/sql-reference.md` |
| 测试覆盖 | README 示例代码可编译运行 |
| 验收标准 | 文档与实现一致 |

---

### PR #29：发布 NuGet 包 TSLite 0.1.0

| 项目 | 内容 |
|------|------|
| 变更点 | 配置 NuGet 打包，发布到 nuget.org |
| 新增文件 | `.github/workflows/publish.yml` |
| 测试覆盖 | 打包后可安装并运行示例 |
| 验收标准 | NuGet 包可用；版本号 `0.1.0`；`CHANGELOG.md` 对应段落更新 |

---

## 里程碑总览

| Milestone | 主题 | PR 范围 |
|-----------|------|---------|
| 0 | 项目脚手架 | #1 ～ #3 |
| 1 | 内存与二进制基础设施 | #4 ～ #6 |
| 2 | 逻辑模型与目录 | #7 ～ #9 |
| 3 | 写入路径 | #10 ～ #13 |
| 4 | 查询路径 | #14 ～ #16 |
| 5 | SQL 前端 | #17 ～ #21 |
| 6 | 压缩与 Compaction | #22 ～ #24 |
| 7 | 单文件容器 | #25 ～ #26 |
| 8 | 性能与发布 | #27 ～ #29 |
