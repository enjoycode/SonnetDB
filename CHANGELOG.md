# Changelog

本项目所有重要变更将记录在此文件中。
格式遵循 [Keep a Changelog 1.1.0](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [SemVer 2.0.0](https://semver.org/lang/zh-CN/)。

## [Unreleased]

### Added
- 新增 `TSLite.Engine.Compaction.CompactionPolicy`：Size-Tiered Compaction 触发策略（Enabled / MinTierSize / TierSizeRatio / FirstTierMaxBytes / PollInterval / ShutdownTimeout）
- 新增 `CompactionPlan` / `CompactionResult`：Compaction 计划与执行结果数据对象
- 新增 `CompactionPlanner`（static）：无副作用的 Size-Tiered 计划生成器；tier 划分公式 `tierIndex = max(0, floor(log_TierSizeRatio(fileLength / FirstTierMaxBytes)) + 1)`
- 新增 `SegmentCompactor`：N 路最小堆合并多个段、按 (SeriesId, FieldName) 写入新段；v1 同 timestamp 全部保留、FieldType 冲突抛 `InvalidOperationException`
- 新增 `CompactionWorker`（internal）：后台 Compaction 工作线程，轮询 Plan + Execute + SwapSegments + 删除旧段
- 新增 `SegmentManager.SwapSegments`：在单一锁内原子地移除旧段 + 打开新段 + 重建索引快照，避免中间状态可见
- `TsdbOptions.Compaction` 新增 `CompactionPolicy` 属性（默认 Default，Enabled=true）
- `Tsdb.Open` 末尾：若 `Compaction.Enabled` 启动 `CompactionWorker`
- `Tsdb.Dispose`：先关 CompactionWorker，再关 FlushWorker
- `Tsdb.AllocateSegmentId()`（internal）：线程安全 SegmentId 分配


- 新增 `TSLite.Engine.BackgroundFlushWorker`（internal）：后台 Flush 工作线程，含信号 + 周期轮询双触发，与同步 FlushNow 共享 `_writeSync` 锁保证互斥
- 新增 `BackgroundFlushOptions`（Enabled / PollInterval / ShutdownTimeout），`Dispose` 严格不泄漏后台线程
- 新增 `WalReplay.ReplayIntoWithCheckpoint`：基于 Checkpoint LSN 两遍扫描跳过冗余 WritePoint，消除崩溃恢复的冗余回放开销
- 新增 `WalReplayResult` record（CheckpointLsn / LastLsn / WritePoints）
- `TsdbOptions.BackgroundFlush` 暴露后台线程开关（默认 Enabled=true）
- `Tsdb.CheckpointLsn` 诊断属性：最近一次 Flush 的 WAL CheckpointLsn
- `Tsdb.Write` 在锁外向 worker 发送非阻塞信号；移除同步 Write 路径中的自动 Flush（由后台线程接管）
- `Tsdb.Open` 改用 `ReplayIntoWithCheckpoint` 替代 `ReplayInto`，支持 WAL 续写正确 LSN

### Added
- 新增 `TSLite.Query.QueryEngine`：合并 MemTable + 多 Segment 的查询执行器；支持原始点查询（`Execute(PointQuery)`）、聚合查询（`Execute(AggregateQuery)`）及批量聚合（`ExecuteMany`）（Milestone 4 完成）
- 新增 `PointQuery` / `AggregateQuery` / `AggregateBucket` / `Aggregator` / `TimeRange` 查询类型
- 支持 Count / Sum / Min / Max / Avg / First / Last 七种聚合函数（Float64 / Int64 / Boolean 字段）
- 支持 `GROUP BY time(...)` 桶聚合（基于 PR #7 的 `TimeBucket`）；空桶不输出
- 内部 N 路有序合并器 `BlockSourceMerger`：段按 SegmentId 升序排列后合并，MemTable 在最末，同 ts 全部 yield（不去重）
- `Tsdb.Query` 属性暴露查询入口（`QueryEngine` 无状态，每次查询时重建 SegmentId→Reader 映射）
- **Milestone 4 完成**：查询路径全面贯通（MemTable + 多段 + 时间过滤 + 7 种聚合 + GROUP BY time）

### Added
- 新增 `TSLite.Storage.Segments.SegmentBlockRef`（readonly struct）：跨段统一的 Block 引用（SegmentId + SegmentPath + BlockDescriptor）
- 新增 `SegmentIndex`（sealed class）：单段内 SeriesId / (SeriesId, FieldName) → BlockDescriptor 索引，含段级时间范围与时间窗二分剪枝
- 新增 `MultiSegmentIndex`（sealed class）：跨段只读联合索引快照；`LookupCandidates` 剪枝顺序：段级时间 → series → field → 段内时间窗二分
- 新增 `TSLite.Engine.SegmentManager`（sealed class）：已打开 SegmentReader 集合 + 索引快照管理器；lock 写 + Volatile 无锁读并发模型
- `Tsdb` 接入 `SegmentManager`：Open 时扫描段构建初始索引，FlushNow 时增量 AddSegment；Dispose 时关闭全部 SegmentReader
- `TsdbOptions` 新增 `SegmentReaderOptions` 属性

### Added
- 启动 Milestone 4：查询路径
- 新增 `TSLite.Storage.Segments.SegmentReader`：不可变段文件只读访问器
  - Open 时校验 Magic / Version / HeaderSize / FooterOffset / IndexCrc32
  - 按 SeriesId / (SeriesId, FieldName) / TimeRange 线性查找 BlockDescriptor
  - `ReadBlock` 返回零拷贝 ref struct `BlockData`
  - `DecodeBlock` / `DecodeBlockRange` 解码出 DataPoint[]
- 新增 `BlockDescriptor`（readonly struct）：描述 Block 元数据与物理位置
- 新增 `BlockData`（readonly ref struct）：零拷贝 Block payload 视图
- 新增 `SegmentReaderOptions`：VerifyIndexCrc / VerifyBlockCrc 选项（默认均启用）
- 新增 `SegmentCorruptedException`：段文件损坏或格式不一致时抛出（含 path + offset）
- 新增 `BlockDecoder`（internal static）：ValuePayloadCodec 的对偶，跨平台读用 BinaryPrimitives LE；支持 Float64 / Int64 / Boolean / String 四种类型及 DecodeRange 时间裁剪

### Added
- 启动 TSLite 引擎门面：`TSLite.Engine.Tsdb`（Open / Write / WriteMany / FlushNow / Dispose），完成 Milestone 3 写入路径闭环（PR #13）
- 新增 `TsdbOptions`：引擎全局配置（RootDirectory / FlushPolicy / SegmentWriterOptions / WalBufferSize / SyncWalOnEveryWrite）
- 新增 `TsdbPaths`：标准磁盘布局路径管理（catalog.tslcat + wal/active.tslwal + segments/{id:X16}.tslseg）
- 新增 `FlushCoordinator`：MemTable → Segment + WAL Checkpoint + WAL Truncate 三步原子可见
- 新增 `WalTruncator.SwapAndTruncate`：rename + 重建策略，避免就地截断的并发风险
- 新增 `SegmentWriterOptions.PostRenameAction`（internal）：原子 rename 完成后的测试钩子，用于模拟 rename 之后崩溃场景
- 完成 Milestone 3：写入路径闭环，三场景崩溃恢复测试矩阵齐全（未 Flush 崩溃 / Flush 后崩溃 / rename 后未 Checkpoint 崩溃）

### Added
- 初始化项目规划文档：`README.md`、`CHANGELOG.md`、`ROADMAP.md`、`AGENTS.md`
- 确定技术栈：C# / .NET 10 / xUnit / BenchmarkDotNet / GitHub Actions
- 确定核心设计原则：Safe-only、Span/MemoryMarshal、InlineArray、WAL+MemTable+Segment
- 解决方案与项目骨架（`TSLite.slnx`、`src/TSLite`、`src/TSLite.Cli`、`tests/TSLite.Tests`、`tests/TSLite.Benchmarks`）（PR #2）
- `Directory.Build.props`（统一 `LangVersion` / `Nullable` / `ImplicitUsings` / `TreatWarningsAsErrors`）
- `Directory.Packages.props`（Central Package Management）
- `global.json`（固定 .NET 10 SDK）
- `.editorconfig`（统一代码风格）
- 新增 GitHub Actions CI 工作流（build + test，ubuntu / windows 矩阵）
- 新增 CodeQL 安全扫描工作流
- 新增 Dependabot 依赖更新配置
- 新增 dotnet format 校验
- 新增时序数据库性能对比基准（`tests/TSLite.Benchmarks/`）：使用 BenchmarkDotNet 0.15.8 对比 TSLite（内存占位）、SQLite、InfluxDB 2.x 和 TDengine 3.x 在相同 Docker 环境下的 100 万条数据**写入、时间范围查询、1 分钟桶聚合**的性能，含 Docker Compose 配置和 README 说明
- 新增 `TSLite.IO.SpanWriter`：基于 Span/MemoryMarshal/BinaryPrimitives 的 safe-only 顺序二进制写入器
- 新增 `TSLite.IO.SpanReader`：基于 Span/MemoryMarshal/BinaryPrimitives 的 safe-only 顺序二进制读取器
- 支持基础类型、unmanaged 结构体、结构体数组、VarInt(LEB128)、字符串的 round-trip 编解码
- 全程 little-endian，零 `unsafe`（PR #4）
- 新增 `TSLite.Buffers.InlineBytes4/8/16/32/64`：基于 `[InlineArray(N)]` 的固定长度内联缓冲区
- 新增 `InlineBytesExtensions`：通过 `MemoryMarshal.CreateSpan` 提供 Safe-only 的 `AsSpan` / `AsReadOnlySpan` 视图
- 新增 `InlineBytesHelpers`：泛型 `SequenceEqual` / `CopyFrom` 辅助方法
- 新增 `TsdbMagic`：定义 TSLite 文件 / 段 / WAL 的 magic 与格式版本常量（PR #5）
- 新增固定二进制结构体（namespace `TSLite.Storage.Format`）：
  - `FileHeader`（64B）/ `SegmentHeader`（64B）/ `BlockHeader`（64B）
  - `BlockIndexEntry`（48B）/ `SegmentFooter`（64B）/ `WalRecordHeader`（32B）
- 新增枚举：`BlockEncoding` / `FieldType` / `WalRecordType`
- 新增 `FormatSizes` 常量类，所有 header 尺寸由编译期 `Unsafe.SizeOf<T>` 测试守护
- 完成 Milestone 1：内存与二进制基础设施（Span/MemoryMarshal/InlineArray + 全部固定 header）（PR #6）
- 新增逻辑数据模型（namespace `TSLite.Model`）：
  - `FieldValue`（readonly struct，零装箱，支持 Float64/Int64/Boolean/String）
  - `Point`（用户层写入对象，含校验规则）
  - `DataPoint`（引擎内单 field 数据点，readonly record struct）
  - `SeriesFieldKey`（series + field 复合键，readonly record struct）
  - `AggregateResult`（Count/Sum/Min/Max/Avg 累加器）
  - `TimeBucket`（时间桶 Floor/Range/Enumerate 辅助）
- 启动 Milestone 2：逻辑模型与 Series Catalog（PR #7）
- 新增 `TSLite.Model.SeriesKey`（readonly struct）：规范化 `measurement + sorted(tags)` 为确定性字符串，格式 `measurement,k1=v1,k2=v2`，tags 按 key Ordinal 升序
- 新增 `TSLite.Model.SeriesId`（static class）：通过 `XxHash64` 将 `SeriesKey.Canonical` 的 UTF-8 编码折叠为 `ulong`，作为引擎主键（PR #8）
- 新增 `TSLite.Catalog.SeriesCatalog`：线程安全的 SeriesKey ↔ SeriesId ↔ SeriesEntry 中央目录（基于 ConcurrentDictionary，单写多读友好）
- 新增 `TSLite.Catalog.SeriesEntry`：序列目录条目（Id / Key / Measurement / Tags / CreatedAtUtcTicks），Tags 以 FrozenDictionary 保证不可变
- 新增 `SeriesCatalog.Find`：按 measurement + tag 子集线性查找
- 新增 `TSLite.Catalog.CatalogFileCodec`：`.tslcat` 目录文件序列化器（含临时文件原子替换写入与规范化校验加载）
- 新增 `TSLite.Storage.Format.CatalogFileHeader`（64B）：目录文件头，含 magic "TSLCATv1" / 版本 / 条目数
- 新增 `TsdbMagic.Catalog`（"TSLCATv1"）与 `TsdbMagic.CreateCatalogMagic()`
- 新增 `FormatSizes.CatalogFileHeaderSize = 64`
- 新增 `InlineBytes24` 内联缓冲区及其 `AsSpan`/`AsReadOnlySpan` 扩展
- 完成 Milestone 2：逻辑模型与 Series Catalog（PR #9）
- 启动 Milestone 3：写入路径（PR #10）
- 新增 `TSLite.Storage.Format.WalFileHeader`（64B）：WAL 文件头，含 magic "TSLWALv1" / 版本 / FirstLsn
- 新增 `FormatSizes.WalFileHeaderSize = 64`
- 更新 `WalRecordHeader`（32B）：新增 `Magic`（0x57414C52）/ `Flags` / `PayloadCrc32` / `Lsn` 字段，移除 `SeriesId` 至 payload
- 更新 `WalRecordType`：重命名 `Write→WritePoint`、`CatalogUpdate→CreateSeries`，新增 `Truncate=4`
- 新增 `TSLite.Wal` 命名空间：
  - `WalRecord` 抽象基类及派生：`WritePointRecord` / `CreateSeriesRecord` / `CheckpointRecord` / `TruncateRecord`
  - `WalWriter`：append-only WAL 写入器，含 CRC32（`System.IO.Hashing.Crc32`）+ fsync 支持
  - `WalReader`：迭代式回放，支持文件尾截断与 CRC 校验失败的优雅停止，暴露 `LastValidOffset`
  - `WalReplay`：将 WAL 回放到 `SeriesCatalog`，并 yield 出 `WritePointRecord` 流
  - `WalPayloadCodec`（internal）：4 种 RecordType × 4 种 FieldType 的 payload 编解码
- 新增 `TSLite.Memory.MemTableSeries`：单 (SeriesId, FieldName, FieldType) 桶，
  支持顺序与乱序追加，Snapshot 稳定排序（`_isSorted` 快速路径 + 索引辅助稳定排序）
- 新增 `TSLite.Memory.MemTable`：以 SeriesFieldKey 为主键的写入内存层，
  支持 WAL Replay 装载（`ReplayFrom`）、阈值触发 Flush（`ShouldFlush`）、Reset 与 SnapshotAll（PR #11）
- 新增 `TSLite.Memory.MemTableFlushPolicy`：MaxBytes / MaxPoints / MaxAge 三种阈值策略
- 新增 `TSLite.Storage.Segments.SegmentWriter`：把 MemTable 写为不可变 `.tslseg` 文件，使用临时文件 + 原子 rename 保证崩溃安全（PR #12）
- 新增 `SegmentWriterOptions`：BufferSize / FsyncOnCommit / TempFileSuffix 写入选项
- 新增 `SegmentBuildResult`：构建结果记录（路径、BlockCount、时间范围、各区偏移、耗时）
- 新增 `ValuePayloadCodec`（internal）：Float64 / Int64 / Boolean / String 的 Raw 编码
- 新增 `FieldNameHash`（internal）：基于 XxHash32 的字段名哈希，用于 BlockIndexEntry.FieldNameHash
- 启用 `BlockHeader.Crc32`（CRC32(FieldNameUtf8 ++ TsPayload ++ ValPayload)）与 `SegmentFooter.Crc32`（IndexCrc32）

---

## [0.1.0] — *Planned*

> 对应 ROADMAP Milestone 0 ～ Milestone 3

### Added
- 解决方案与项目骨架（`TSLite.sln`、`src/TSLite`、`src/TSLite.Cli`、`tests/TSLite.Tests`、`tests/TSLite.Benchmarks`）
- `.editorconfig`、`Directory.Build.props`（统一 `LangVersion` / `Nullable` / `TreatWarningsAsErrors`）
- GitHub Actions CI（build + test，矩阵 ubuntu-latest / windows-latest）
- `SpanReader` / `SpanWriter`（`ref struct`，基于 `BinaryPrimitives` + `MemoryMarshal`）
- `[InlineArray]` 工具：`Magic8`、`Reserved16` 等固定缓冲
- 核心 `unmanaged struct`：`FileHeader`、`SegmentHeader`、`BlockHeader`、`BlockIndexEntry`、`SegmentFooter`
- 逻辑模型：`Point`、`DataPoint`、`SeriesFieldKey`、`AggregateResult`
- `SeriesKey` 规范化 + `SeriesId`（XxHash64）
- `SeriesCatalog`（内存 + 持久化）
- `WalWriter` / `WalReader`（append-only + replay）
- `MemTable`
- `SegmentWriter`（BlockHeader + payload + footer index）
- Flush 流程：MemTable → Segment，WAL truncate

---

## [0.2.0] — *Planned*

> 对应 ROADMAP Milestone 4 ～ Milestone 5

### Added
- `SegmentReader`（按 seriesId/time range 裁剪 block）
- `QueryEngine.QueryRaw`（合并 MemTable + 多 Segment）
- 聚合：`min/max/sum/avg/count` + 时间桶 `time(10s)` 分组
- SQL 词法与语法分析器（手写递归下降）
- `CREATE MEASUREMENT` / `INSERT INTO ... VALUES` / `SELECT ... WHERE ... GROUP BY time(...)` 语句支持
- ADO.NET 风格 API：`TsdbConnection / TsdbCommand / TsdbDataReader`

---

## [0.3.0] — *Planned*

> 对应 ROADMAP Milestone 6 ～ Milestone 8

### Added
- 时间戳 delta 编码（block payload V2）
- 值列 delta 编码
- `CompactionEngine`（合并旧 segment）
- page manager + free list
- 将 manifest / wal / segments 合并为单一 `.tsl` 文件
- BenchmarkDotNet 基准（写入/查询/聚合）
- 发布 NuGet 包 `TSLite` 0.1.0

---

[Unreleased]: https://github.com/maikebing/TSLite/compare/HEAD...HEAD
[0.1.0]: https://github.com/maikebing/TSLite/releases/tag/v0.1.0
[0.2.0]: https://github.com/maikebing/TSLite/releases/tag/v0.2.0
[0.3.0]: https://github.com/maikebing/TSLite/releases/tag/v0.3.0
