# ROADMAP

本文件描述 SonnetDB 的分批 PR 开发计划，按 Milestone 组织。每个 PR 均包含：变更点、新增文件、测试覆盖与验收标准。

> **状态注记**：本路线图于 PR #20 合并后做过一次大幅修订。原计划在 Milestone 5 直接进入 SQL 前端，但实际开发中插入了"稳定性与性能（写入侧）"工作（后台 Flush / Compaction / 多 WAL 滚动 / DELETE-Tombstone）。SQL 前端已顺延到 Milestone 6，原 Milestone 6/7/8 编号顺移。

图例：✅ 已完成 / 🚧 进行中 / 📋 计划中

---

## Milestone 0 — 项目脚手架 ✅

| PR | 主题 | 状态 |
|----|------|------|
| #1 | 初始化规划文档（README / CHANGELOG / ROADMAP / AGENTS） | ✅ |
| #2 | 解决方案与项目骨架（`net10.0` + Nullable + TreatWarningsAsErrors） | ✅ |
| #3 | GitHub Actions CI（ubuntu + windows 双矩阵） | ✅ |

---

## Milestone 1 — 内存与二进制基础设施（Safe-only） ✅

| PR | 主题 | 状态 |
|----|------|------|
| #4 | `SpanReader` / `SpanWriter`（基于 `BinaryPrimitives` + `MemoryMarshal`） | ✅ |
| #5 | `[InlineArray]` 工具：`InlineBytes8/16/32` + `TsdbMagic` | ✅ |
| #6 | 核心 unmanaged struct：`SegmentHeader` / `BlockHeader` / `BlockIndexEntry` / `SegmentFooter` / `WalRecordHeader` / `WalFileHeader` / `FormatSizes` | ✅ |

---

## Milestone 2 — 逻辑模型与目录 ✅

| PR | 主题 | 状态 |
|----|------|------|
| #7 | 核心数据模型：`Point` / `DataPoint` / `FieldValue` / `SeriesFieldKey` / `AggregateResult` / `TimeBucket` | ✅ |
| #8 | `SeriesKey` 规范化 + `SeriesId = XxHash64(series_key)` | ✅ |
| #9 | `SeriesCatalog` + `CatalogFileCodec`（持久化 catalog 文件） | ✅ |

---

## Milestone 3 — 写入路径 ✅

| PR | 主题 | 状态 |
|----|------|------|
| #10 | `WalWriter` / `WalReader` / `WalReplay` / `WalRecord`（Append-only WAL + CRC + 截断容忍） | ✅ |
| #11 | `MemTable` / `MemTableSeries` / `MemTableFlushPolicy` | ✅ |
| #12 | `SegmentWriter` / `SegmentBuildResult` / `ValuePayloadCodec`（不可变 `.SDBSEG`：临时文件 + 原子 rename） | ✅ |
| #13 | `Tsdb` 引擎门面 + `FlushCoordinator` + `WalTruncator` + `TsdbPaths`（写入路径闭环；崩溃恢复矩阵齐全） | ✅ |

磁盘布局（M3 落地）：
```
<root>/
  catalog.SDBCAT
  wal/active.SDBWAL
  segments/{id:X16}.SDBSEG
```

---

## Milestone 4 — 查询路径 ✅

| PR | 主题 | 状态 |
|----|------|------|
| #14 | `SegmentReader`：零拷贝只读访问 + 完整一致性校验（Magic / Version / FooterOffset / IndexCrc / BlockCrc） | ✅ |
| #15 | `SegmentIndex` / `MultiSegmentIndex` / `SegmentManager`：多段查询索引层 + 跨段时间窗剪枝 + Volatile 发布的无锁读 | ✅ |
| #16 | `QueryEngine`：MemTable + 多段 N 路堆合并 + 7 种聚合（Count/Sum/Min/Max/Avg/First/Last） + `GROUP BY time(...)` 桶聚合 | ✅ |

> 注：原计划中的 PR #15（"QueryEngine.QueryRaw"）已合并进 PR #16；新 PR #15 替换为查询所需的多段索引层。

---

## Milestone 5 — 稳定性与性能（写入侧）

| PR | 主题 | 状态 |
|----|------|------|
| #17 | 后台异步 Flush 工作线程 `BackgroundFlushWorker` + Checkpoint LSN 驱动的 WAL Replay 跳过（消除冗余回放） | ✅ |
| #18 | Size-Tiered Segment Compaction：`CompactionPlanner` / `SegmentCompactor` / `CompactionWorker` + `SegmentManager.SwapSegments` | ✅ |
| #19 | 多 WAL 滚动（segmented WAL）：`WalSegmentSet` / `WalRollingPolicy` + Legacy `active.SDBWAL` 自动升级 | ✅ |
| #20 | 删除支持（DELETE / Tombstone + WAL Delete 记录 + `tombstones.tslmanifest` + Compaction 阶段消化） | ✅ |
| #21 | Retention TTL：按策略自动注入墓碑 + 过期段直接 drop | ✅ |

磁盘布局（M5 落地后）：
```
<root>/
  catalog.SDBCAT
  tombstones.tslmanifest
  wal/{startLsn:X16}.SDBWAL             # 多 segment 滚动
  segments/{id:X16}.SDBSEG
```

---

## Milestone 6 — SQL 前端 + Tag 倒排索引

| PR | 主题 | 状态 |
|----|------|------|
| #22 | SQL 词法 / 语法分析器（递归下降，无第三方依赖；AST 节点） | ✅ |
| #23 | `CREATE MEASUREMENT` + schema 持久化 | ✅ |
| #24 | `INSERT INTO ... VALUES (...)`（含批量、TAG/FIELD 类型校验、时间戳缺省） | ✅ |
| #25 | `SELECT ... WHERE ... GROUP BY time(...)`（含 tag 过滤、聚合下推） | ✅ |
| #26 | `DELETE FROM ... WHERE time >= a AND time <= b`（落到 PR #20 的 Tombstone） | ✅ |
| #27 | Tag 倒排索引：`(tagKey, tagValue) → [SeriesId]`（加速 WHERE tag=...） | ✅ |
| #28 | 按照标准的ADO.NET  API, 实现`SndbConnection / SndbCommand / SndbDataReader`等等。 | ✅ |

---

## Milestone 7 — 压缩编码

| PR | 主题 | 状态 |
|----|------|------|
| #29 | 时间戳 Delta-of-Delta 编码（block payload V2，向后兼容 V1） | ✅ |
| #30 | 数值列 Gorilla / XOR 编码（Double） + RLE（Bool） + 字典（String） | ✅ |
| #31 | 块级压缩开关与统计：在 `SegmentWriter.Options` 暴露编码选择，`SegmentReader` 自动按 `BlockEncoding` 解码 | ✅ |

> 注：BlockEncoding 字段 PR #6 已预留；本 Milestone 真正启用 Delta / Gorilla。

---

## Milestone 8 — 服务器模式

> **进入条件**：PR #21（Retention TTL）与 PR #35（嵌入式模式 BenchmarkDotNet 基准）必须先完成，建立性能基线后再服务化，避免后续无法归因 HTTP / 序列化 / 引擎本身的性能差异。

### 设计要点（PR #32）

- **运行时形态**：AOT-friendly Minimal API（`net10.0` + `PublishAot=true`），单进程；项目位于 `src/SonnetDB/`，引用 `SonnetDB`。
- **多租户隔离**：进程内 `ConcurrentDictionary<string, Tsdb>` 注册表，一个数据库 = 一个子目录 + 一个 `Tsdb` 实例，复用现有 `BackgroundFlushWorker` / `CompactionWorker`。`CREATE DATABASE <name>` 创建子目录并注册；`DROP DATABASE` 通过引用计数 + `Dispose` 回收。**不**采用子进程隔离（与 AOT min api 风格冲突且开销过大）。
- **协议（仅 HTTP，先不做 WebSocket）**：
  - `POST /v1/db/{db}/sql` 提交单条 SQL；请求体 `application/json`，包含 `sql` 与可选 `parameters`。
  - `POST /v1/db/{db}/sql/batch` 批量 INSERT（直接走 ADO.NET 的 batch 接口，零额外解析）。
  - 结果集采用 **`application/x-ndjson` 流式输出**，配合 `System.Text.Json` AOT source generator，避免 JIT 反射 + 全量缓冲；这是 AOT + 大结果集场景下吞吐最佳的组合。
  - WebSocket 推后评估：仅当出现真实的"订阅 / 长查询流"需求再加（订阅语义本身需要引擎层支持，目前不具备）。
- **认证（极简）**：单层 `Authorization: Bearer <token>` + 配置文件里的静态 token 列表（角色仅 `admin` / `readwrite` / `readonly`）。不实现 SQL 级 `CREATE USER / GRANT`，避免引入控制面元数据。等到真有多用户场景再升级。
- **可观测性**：`/healthz`、`/metrics`（Prometheus 文本格式，最少包含 per-db 写入速率、Flush/Compaction 次数、活跃 segment 数）。

### PR 列表

| PR | 主题 | 状态 |
|----|------|------|
| #32 | `SonnetDB`：AOT Minimal API + 多 `Tsdb` 实例注册表 + `POST /v1/db/{db}/sql` + ndjson 流式结果 + Bearer token 三角色认证 + `/healthz` + `/metrics` | ✅ |
| #33 | 远端 ADO.NET 客户端 `SonnetDB.Data`：与 PR #28 共享 `SndbConnectionStringBuilder`，通过 scheme（`sonnetdb://` 本地、`sonnetdb+http://` 远程）切换实现，结果集流式反序列化 | ✅ |
| #34a | 服务端控制面：用户/权限存储 + SQL DDL（CREATE/ALTER/DROP USER、GRANT/REVOKE、CREATE/DROP DATABASE）+ `POST /v1/auth/login` 颁发动态 token + Bearer 中间件接入 `UserStore` | ✅ |
| #34b | Vue3 管理后台（Naive UI）：登录页、数据库列表/状态、SQL 控制台、用户/权限/Token 管理 | ✅ |
| #34c | 实时推送：基于 SSE 的指标 / 慢查询 / 数据库事件流，前端订阅自动刷新 | ✅ |


---

## Milestone 9 — 性能与发布

| PR | 主题 | 状态 |
|----|------|------|
| #35 | BenchmarkDotNet：写入 / 查询 / 聚合 / Compaction 基准，编写评测 InfluxDB TDengine SQLite SonnetDB  SonnetDB 等五个时序数据库的各项指标对比， 并连同机器性能都写在readme.md 里面。  | ✅|
| #36 | `SonnetDB` Docker 性能测试：补齐 `src/SonnetDB/Dockerfile`、基准用 `docker-compose` 环境、`ServerBenchmark`（写入 / 查询 / 聚合）与 README 中的服务端性能基线。 | ✅ |
| #37a | 文档完善（已落地部分）：重写 `README.md` / `README.en.md`，补齐 `docs/getting-started.md` / `docs/data-model.md` / `docs/sql-reference.md` / `docs/file-format.md` 及发布文档，使用 JekyllNet 构建内置 `/help` 帮助站点，核对路线图与当前代码/功能，清理过时说明。 | ✅ |
| #37b | 文档发布：将 JekyllNet 文档站点接入 GitHub Pages 自动构建与发布流水线，支持从同一套 `docs/` 源码同时产出服务端 `/help` 站点与 Pages 静态站点，避免文档仅内置在 `SonnetDB` 镜像中。 | ✅ |
| #38 | 发布 NuGet 包 `SonnetDB 0.1.0` + `.github/workflows/publish.yml`，打包生成一套包含 `SonnetDB`、`SonnetDB.Data`、`SonnetDB.Cli` 的 SDK Bundle，并附带使用说明；发布 Windows 和 Linux 版本；再打包 `SonnetDB` 完整 Bundle，包含前端、`SonnetDB.Cli`、`SonnetDB.Data` 等，能够一键启动；同时生成 Windows `msi` 与 Linux `deb` / `rpm` 安装包。 | ✅ |
| #39 | Docker 服务端模式镜像自动发布：新增 GitHub Actions 工作流，自动构建并推送 `SonnetDB` 镜像到 `iotsharp/sonnetdb` 与 `ghcr.io/<owner>/sonnetdb`，补齐标签策略、运行说明与 Secrets 要求。 | ✅ |
---
## Milestone 10 — 扩展和第三方

| PR | 主题 | 状态 |
|----|------|------|
| #40 |  实现cil的能力， 能够操作和打开本地文件， 可以连接sndb.server ， 评估是否考虑使用 SonnetDB.Data 来实现，或者是直接调用接口实现。 | 📋 |
| #41 |  SonnetDB 支持 订阅MQTT消息，通过后台管理来添加订阅。   | 📋 |
| #42 | 批量入库快路径核心库 `SonnetDB.Ingest`：协议嗅探（Detector）+ 三协议 reader（LineProtocol / JSON / Bulk INSERT VALUES）+ `BulkIngestor` 统一消费入口（ArrayPool 8192 批 → `Tsdb.WriteMany`，支持 FailFast/Skip 与可选 FlushOnComplete）。绕开每条 INSERT 的 SQL Lexer→Parser→Planner 开销，为大批量写入提供基础；`src/SonnetDB` 仍保持零第三方运行时依赖。 | ✅ |
| #43 | `SonnetDB.Data` 接入：`SndbCommand.CommandType = CommandType.TableDirect` 走批量入库快路径；`IConnectionImpl.ExecuteBulk` + `EmbeddedConnectionImpl` 桥接 `Tsdb.Measurements` 的 schema 到 `BulkValuesReader` 的列角色 resolver；嵌入式连接零拷贝直达 `BulkIngestor`。 | ✅ |
| #44 | `SonnetDB` 远程批量端点：`POST /v1/db/{db}/measurements/{m}/lp\|json\|bulk` 三个端点 + `RemoteConnectionImpl.ExecuteBulk`；保留 SQL 路径不变。 | ✅ |
| #45 | 批量入库基准：在 `SonnetDB.Benchmarks` 新增 `BulkIngestBenchmark`，对比 SQL INSERT 单点 / TableDirect LP / TableDirect JSON / TableDirect Bulk VALUES，刷新 README 写入吞吐对比表。 | ✅ |

---
## Milestone 11 — 写入快路径（PR #45 瓶颈收收尾）

> **背景**：PR #45 实测发现 100k 点下嵌入式 LP/JSON/Bulk 三路与 SQL VALUES baseline 吞吐几乎打平（~170–200ms），内存仅节省 25～42%；
> 服务端 `/sql/batch` 1M 点 ~21s vs 嵌入式 0.62s，差 33.8×。调用链详细剖析定位出三个主要瓶颈：
> 1. `Tsdb.WriteMany(IEnumerable<Point>)` 是假批量，逐点 lock + 逐字段 WAL record；
> 2. 服务端 LP/Bulk payload `Encoding.UTF8.GetString` + `JsonPointsReader.ToArray()` 二次拷贝；
> 3. 端点默认 `flush=true` 同步落盘占用 RTT。
>
> **目标**：嵌入式 100k 点 ≤ 80ms（1M 点 ≤ 300ms），服务端 LP/Bulk 达到 ≥ 700k pts/s。

| PR | 主题 | 状态 |
|----|------|------|
| #46 | **引擎真批量**（已落地，最小切片）：`Tsdb.WriteMany(ReadOnlySpan<Point>)` 整批仅取一次 `_writeSync` 锁、批末仅 `Signal` 一次；`WriteMany(IEnumerable<Point>)` 自动嗅探 `Point[]` / `List<Point>` / `ArraySegment<Point>` 下沉到 span 重载。**WAL 记录格式与 `FileHeader.Version` 保持不变**（向后兼容；`WalRecordType.WriteBatch` 实测 ROI 偏低，留给后续按需追加）。`BulkIngestor`、三端点、`RemoteConnectionImpl` 自动受益。基准（100k 点）：Mean 持平、**Allocated −42~58%**。 | ✅ |
| #47 | **服务端 + Reader 零拷贝**：`BulkIngestEndpointHandler.ReadAllAsync` 改 `ArrayPool<byte>` 租借（精确长度优先，未知则翻倍扩容），消除 LOH；`JsonPointsReader` 字段重构为 `ReadOnlyMemory<byte> _utf8Memory + byte[]? _pooledBuffer`，ROM ctor 零拷贝持有 caller buffer，string ctor 走 ArrayPool；`BulkIngestEndpointHandler.HandleAsync` JSON 直接喂 `ReadOnlyMemory<byte>`，LP 走 `ArrayPool<char>` rent + `Encoding.UTF8.GetChars`，BulkValues 用精确长度 `GetString(buffer,0,length)`；三端点追加 `DisableRequestSizeLimitAttribute` 解除 Kestrel 30MB 上限。**基准（1M 点 / 本地 dotnet run）**：LP `1.20s / 52MB`、JSON `1.20s / 71MB`、Bulk `1.10s / 34MB`、`/sql/batch` `5.09s / 668MB`，三端点 ~17–19× faster vs PR #45 baseline、alloc −89~95%。Reader 接口仍保 `ROM<char>` / `string`，byte 化留作未来独立 PR。 | ✅ |
| #48 | **端点 flush 三档位**：`?flush=false\|true\|async`，默认 `false`（最快，仅入 MemTable+WAL）；`async` 走新 `Tsdb.SignalFlush()` 仅向 `BackgroundFlushWorker.Signal()` 发信号后立即返回（未启用后台 Flush 时降级为同步 `FlushNow`）；`true|sync|yes|1` 保持同步 `FlushNow`。新增 `BulkFlushMode { None, Async, Sync }` 枚举与 `BulkIngestor.Ingest` 新主重载（旧 `bool flushOnComplete` 重载向后兼容）。`BulkIngestEndpointHandler.ParseFlush` + ADO `EmbeddedConnectionImpl.ParseFlushMode` 同步解析；`RemoteConnectionImpl` 自然透传 query string。补齐三档位 × 三端点端到端 + BulkIngestor 直测，全量回归 1241 + 97 通过。 | ✅ |
| #49 | **基准刷新 + 对外对比**（写入快路径专题收尾）：
 - ✅ README 「写入：100 万点」表新增 PR #47 服务端 LP/JSON/Bulk 三行（1.10–1.20 s / 34–71 MB / ~1.77–1.93× vs 嵌入式）；
 - ✅ README 「嵌入式 vs SonnetDB」同机对比表拆分为 SQL Batch + LP/JSON/Bulk 四行；
 - ✅ README 「批量入库快路径」补充 PR #48 `?flush=` 三档位表（None / Async / Sync 语义与适用场景）；
 - ✅ 新增 `InsertBenchmark.TDengine_InsertSchemaless_1M` + `TDengineRestClient.WriteLineProtocolAsync(db, lp, precision)`，走 TDengine InfluxDB-compat `POST /influxdb/v1/write?precision=ms`，按 100k 行/批切片；
 - ✅ 全量重跑 **24 个基准**（i9-13900HX / .NET 10.0.6 / Docker WSL2，~20 分钟）并把真实数字写进 `tests/SonnetDB.Benchmarks/README.md`：Insert SonnetDB **545 ms / 530 MB**、SQLite 811 ms / 465 MB、InfluxDB 5,222 ms / 1,457 MB（9.58×）、TDengine REST 44,137 ms / 156 MB（81×）、**TDengine schemaless LP 996 ms / 61 MB（1.83×）**〔同库 schemaless 比 REST INSERT 子表路径快 44× / 分配缩到 39%〕；Query SonnetDB 6.71 ms、Aggregate 42.3 ms、Compaction 16.3 ms；
 - ✅ 重建 `iotsharp/sonnetdb:bench` 镜像后首次跑通 **ServerInsertBenchmark 全部 4 个路径**：SQL Batch `19.80 s / 655 MB`、LP `1.293 s / 52 MB`、JSON `1.352 s / 71 MB`、Bulk `1.120 s / 34 MB`——PR #47 三端点稳定进入「秒级 1M 点 + ≤ 80 MB 分配」区间，比 SQL Batch 快 15–7×、分配缩到 5–11%，比嵌入式仅多 ~2.0–2.5×额外开销。 | ✅ |

**推进顺序**：PR #46 ✅ → PR #47 ✅ → PR #48 ✅ → PR #49 ✅。Milestone 11 「写入快路径」专题完整收尾：嵌入式写入达到 ~1.83 M pts/s（545 ms / 1M 点）；服务端三端点 LP/JSON/Bulk 全部重跑通过后仍保持 1.12–1.35 s / 34–71 MB，远超 Milestone 11 原定 ≥ 700k pts/s 目标；对外同机粗略对比表明 SonnetDB 写入比 InfluxDB 快 **9.6×**、比 TDengine REST INSERT 快 **81×**、比 TDengine schemaless LP 快 **1.83×**，范围查询比 InfluxDB 快 **61×**、比 SQLite 快 **6.6×**。

---

## Milestone 12 — 函数与算子扩展（PID / Forecast / UDF）

> **背景**：当前 `Aggregator` 是 `enum`（7 个内置聚合）+ `AggregateResult` 单累加器结构，已经无法承载 stddev / percentile / derivative / PID / forecast 等函数族。本里程碑把"函数"提升为一等公民，引入 `FunctionRegistry` + `IAggregateFunction` + `WindowOperator` + `TableValuedFunction` 四类扩展点，并以 **PID 与 Forecast** 作为首批内置示例，建立 SonnetDB 在工业 / IoT / 可观测性场景的差异化能力。
>
> **设计原则**：
> 1. **零破坏**：现有 7 个聚合迁移到 `IAggregateFunction`，对外 SQL / ADO.NET / Server 行为不变。
> 2. **贴合现有架构**：复用 `AggregateResult.Merge` 的 mergeable accumulator 模型，与 MemTable + N 路堆合并 + 跨段聚合天然兼容。
> 3. **AOT 友好**：内置函数 `sealed class` + 静态注册，零反射，与 `SonnetDB` 的 `PublishAot=true` 路线兼容。
> 4. **Dogfooding**：PID / Forecast 自身使用 UDF 接口实现，验证 API 设计合理性。

### Tier 划分

| Tier | 主题 | 代表函数 |
|------|------|----------|
| 1 | 标量 / 逐点函数 | `abs` `round` `sqrt` `log` `coalesce` `case when` `cast` `time_bucket` `date_trunc` `extract` |
| 2 | 扩展聚合 | `stddev` `variance` `percentile` `p50/p90/p95/p99` `median` `mode` `spread` `distinct_count(HLL)` `tdigest_agg` `histogram` |
| 3 | 时序窗口算子 | `derivative` `non_negative_derivative` `difference` `integral` `moving_average` `ewma` `cumulative_sum` `rate` `irate` `increase` `delta` `holt_winters` `interpolate` `fill` `locf` `state_duration` `state_changes` |
| 4 | 控制与预测 | `pid(value, setpoint, kp, ki, kd)` `pid_series(...)` `forecast(...)` `anomaly(...)` `changepoint(...)` `dtw_distance(...)` |
| 5 | UDF 扩展点 | `RegisterScalarFunction` `RegisterAggregateFunction` `RegisterTableValuedFunction` |

### PR 列表

| PR | 主题 | 状态 |
|----|------|------|
| #50 | **`FunctionRegistry` + `IAggregateFunction` 基础设施**：新增 `src/SonnetDB/Query/Functions/`，定义 `IAggregateFunction` / `IAggregateState` / `FunctionRegistry`；把现有 7 个聚合（Count/Sum/Min/Max/Avg/First/Last）迁移为内置实现；保留 `enum Aggregator` 作为内部 fast-path 兼容层；现有 SQL / ADO.NET / Server / Benchmark 行为完全不变 | ✅ |
| #51 | **Tier 1 标量函数 + SQL 函数调用表达式**：SQL Parser/AST 增加 `FunctionCallExpr`，binder 阶段查 `FunctionRegistry` 区分 标量 / 聚合 / 窗口 / TVF；落地数学 / 时间 / 逻辑 / `cast` / `time_bucket` / `date_trunc` / `extract` 等 ~20 个标量函数 | ✅ |
| #52 | **Tier 2 扩展聚合**：`stddev` `variance` `percentile/p50/p90/p95/p99` `median` `mode` `spread` `distinct_count(HLL)` `tdigest_agg` `histogram`；`tdigest` 与 `HLL` 必须实现可合并 `Merge`，跨段聚合 / `GROUP BY time(...)` 桶聚合一致 | ✅ |
| #53 | **Tier 3 窗口算子框架**：新增 `src/SonnetDB/Query/Window/WindowOperator`，支持基于点数 N 和基于时间 `RANGE INTERVAL` 的滑动窗口；落地 `derivative` `non_negative_derivative` `difference` `integral` `moving_average` `ewma` `cumulative_sum` `rate` `irate` `increase` `delta` `holt_winters` `interpolate` `fill` `locf` `state_duration` `state_changes` | ✅ |
| #54 | **PID 内置函数 + 参数估算 + 控制回写示例**：聚合形态 `pid(value, setpoint, kp, ki, kd)` 在 `GROUP BY time(...)` 桶内输出最终 u(t)；行级窗口形态 `pid_series(...)` 输出每行 u(t) 用于回测；状态结构 `{ integral, prevError, prevTimeMs }`，跨段 `Merge` 按时间序拼接；**新增 `PidParameterEstimator.Estimate`（纯 C# 阶跃响应辨识）**：基于 Sundaresan & Krishnaswamy 35%/85% 两点法拟合 FOPDT 模型，支持 Ziegler-Nichols / Cohen-Coon / Skogestad IMC 三种整定规则，直接从历史时序数据推算 Kp / Ki / Kd；当前先以嵌入式/库级 API 交付，后续再接入 `FunctionRegistry` 暴露为 SQL 可查询函数；新增 `docs/pid-control.md` 端到端教程 + `INSERT … SELECT pid_series(...)` 控制回写示例 | ✅ |
| #55 | **Forecast TVF + 异常 / 变点检测**：表值函数 `forecast(measurement, field, horizon, 'algo'[, season])` 内置 **线性外推 + Holt-Winters**（纯 C#，无外部依赖），返回 `(time, value, lower, upper, ...tags)`；`anomaly(x, 'zscore|mad|iqr', threshold)` `changepoint(x, 'cusum'[, drift])`；ARIMA / Prophet 留给 UDF；新增 `docs/forecast.md` | ✅ |
| #56 | **UDF 注册 API**：`Tsdb.Functions.RegisterScalar(name, Func<...>)` / `RegisterAggregate(IAggregateFunction)` / `RegisterWindow(IWindowFunction)` / `RegisterTableValuedFunction(...)`；嵌入式默认启用，Server 端默认禁用 UDF（仅内置函数）以保证 AOT；新增 `docs/extending-functions.md` | ✅ |
| #57 | **函数基准 + README 函数支持矩阵**：在 `tests/SonnetDB.Benchmarks` 扩展 `AggregateBenchmark`，对比 InfluxDB `derivative` / `holt_winters`、Timescale `time_weight`、TDengine `forecast`；README 新增「支持的 SQL 函数」矩阵表 | ✅ |

### SQL 用法预览

```sql
-- Tier 2：扩展聚合
SELECT time_bucket('1m', time) AS minute,
       avg(usage), p95(usage), stddev(usage), spread(usage)
FROM cpu WHERE host = 'server-01' AND time > now() - 1h
GROUP BY minute;

-- Tier 3：速率与平滑
SELECT time, host,
       rate(bytes_in, 1s) AS bps,
       ewma(temperature, 0.2) AS temp_smooth
FROM nic WHERE time > now() - 5m;

-- Tier 4：PID 控制律回写
INSERT INTO actuator (time, device, valve)
SELECT time, device,
       pid_series(temperature, 75.0, 0.6, 0.1, 0.05) AS valve
FROM reactor WHERE time > now() - 1m;

-- Tier 4：预测
SELECT * FROM forecast(
    (SELECT time, value FROM meter WHERE device='m1' AND time > now()-7d),
    horizon => 1440, algo => 'holt_winters', season => 1440);
```

### 嵌入式 UDF 注册预览（Tier 5）

```csharp
using var db = Tsdb.Open(new TsdbOptions { RootDirectory = "./data" });

db.Functions.RegisterScalar("c2f",
    args => FieldValue.Float64(args[0].AsDouble() * 9 / 5 + 32));

db.Functions.RegisterAggregate(new KalmanAggregate()); // 实现 IAggregateFunction
```

**推进顺序**：PR #50 → #51 → #52 → #53 → #54 → #55 → #56 → #57。
其中 PR #50 是基础设施重构，必须先合并；PR #54 / #55 / #56 是对外差异化卖点，建议在 Milestone 9（发布）完成后立刻推进。

---

## Milestone 13 — 向量类型与嵌入式向量索引（Copilot 知识库底座）

> **背景**：Milestone 14 的 SonnetDB Copilot（智能体）需要一个"零外部依赖"的向量召回能力。我们已经决定 **dogfooding——把向量库做到 SonnetDB 自己里**，而不是引入 SQLite/sqlite-vec / Qdrant 等外部组件。这样既能复用 WAL/Segment/Compaction 的存储栈，也能成为 SonnetDB 的对外差异化能力（"时序 + 向量"二合一）。
>
> **设计原则**：
> 1. **Safe-only 仍生效**：向量距离计算优先使用 `System.Numerics.Tensors.TensorPrimitives`（.NET 10 已内置 SIMD，零 `unsafe`）。
> 2. **零破坏**：新增 `VECTOR(dim)` 字段类型，复用 `FieldValue` 的 union 结构（新增 `Vector` 分支），现有 schema / 写入 / 查询路径保持兼容。
> 3. **AOT 友好**：内置距离函数 + 索引算子均为 `sealed class`，不引入反射或动态代码生成。
> 4. **可演进**：第一版用 brute-force 顺扫 + 段内裁剪，足够覆盖 Copilot 知识库（< 50k 切片）；HNSW 留到 PR #61 按需追加。

### PR 列表

| PR | 主题 | 状态 |
|----|------|------|
| #58 | **`VECTOR(dim)` 数据类型 + 编解码**：`FieldValue` 新增 `Vector` 分支（`ReadOnlyMemory<float>` + dim 校验）；`SegmentWriter` / `SegmentReader` 新增 `BlockEncoding.VectorRaw`（dim×4 字节定长）；schema 在 `CREATE MEASUREMENT` 中支持 `embedding VECTOR(384)` 列；INSERT 支持 `[0.1,0.2,...]` 字面量与参数化 `float[]`；`FileHeader.Version` 升级到 v3 并保留对 v2 的只读回退。<br/>**进度**：a) `FieldType.Vector` + `FieldValue.Vector` + WAL `WritePoint` 编解码 ✅；b) Schema VECTOR(dim) 列 + SQL 字面量 📋；c) `BlockEncoding.VectorRaw` + `FileHeader` v3 升级 📋。 | 🚧 |
| #59 | **向量距离函数（Tier 1 标量 + Tier 2 聚合）**：基于 `TensorPrimitives.CosineSimilarity` / `Distance` / `DotProduct` 实现 `cosine_distance(a,b)` `l2_distance(a,b)` `inner_product(a,b)` `vector_norm(a)`；新增聚合 `centroid(vec)`（按维度求均值，可合并）；`SqlParser` 支持 `<=>` `<->` `<#>` 三个 PostgreSQL/pgvector 兼容运算符（解析为对应函数调用） | 📋 |
| #60 | **`KNN` 表值函数 + 段内 brute-force 召回**：新增 `knn(measurement, column, query_vector, k[, metric])` TVF，返回 `(time, distance, ...tags, ...fields)`；执行器在 `SegmentManager` 上做"段级时间窗剪枝 → 段内顺扫 + 维护大小为 k 的最小堆"；MemTable 也参与召回；首版无 ANN，仅靠并行 + SIMD；`docs/vector-search.md` 给出端到端用法示例 | 📋 |
| #61 | **HNSW 段内 ANN 索引（可选构建）**：`SegmentWriter` 在 flush/compaction 阶段对 `VECTOR` 列可选构建 HNSW 图（`SDBVIDX` 边表 sidecar 文件，不污染 `.SDBSEG`）；`SegmentReader` 检测到 `.SDBVIDX` 自动启用 ANN，否则降级为 brute-force；通过 `CREATE MEASUREMENT (... embedding VECTOR(384) WITH INDEX hnsw(m=16, ef=200))` 声明 | 📋 |
| #62 | **向量基准 + 对比**：`tests/SonnetDB.Benchmarks` 新增 `VectorRecallBenchmark`，10k / 100k / 1M 384-dim 向量的 brute-force 顺扫 vs HNSW 召回延迟 + Recall@10；与 sqlite-vec、pgvector（IVF/HNSW）粗略同机对比写入 README | 📋 |

**推进顺序**：PR #58（类型）→ #59（距离函数）→ #60（KNN 表值函数）→ Milestone 14 可以开始；#61（HNSW）与 #62（基准）允许与 Milestone 14 并行推进。

---

## Milestone 14 — SonnetDB Copilot：MCP 工具 + 知识库 + 智能体

> **背景**：当前服务端 `/mcp/{db}` 已经暴露只读 MCP 工具（`query_sql` / `list_measurements` / `describe_measurement`）+ 三个 schema/stats 资源。在此之上，我们要构建一个**真正能"对话操作 SonnetDB"的智能体**，目标是让用户用自然语言完成"看 schema → 写 SQL → 解释结果 → 调优 / 排错 / 预测"全链路。
>
> **架构总览**：
> ```
> [Web Admin Chat / 第三方 MCP Host]
>           │
>           ▼
>     SonnetDB（命名空间 SonnetDB.Copilot，Microsoft Agent Framework）
>           │
>     ┌─────┼──────────────────────────┐
>     ▼     ▼                          ▼
> Skills 库   Knowledge 检索      MCP Tool 调用
> (剧本/Prompt) (向量召回 ← M13)    (本进程内复用 MCP 工具)
>           │
>           ▼
>     SonnetDB Engine（Tsdb / SQL / Schema）
> ```
>
> **设计原则**：
> 1. **Agent SDK = Microsoft Agent Framework**（与 .NET 10 / AOT 生态原生契合，可直接 host MCP client）。
> 2. **知识库存储 = SonnetDB 自身**（依赖 Milestone 13 的 `VECTOR` + `knn(...)`，自我 dogfooding）。
> 3. **嵌入模型多供应商兼容**：
>    - **本地 ONNX**（默认 `bge-small-zh-v1.5` int8，~30 MB，CPU 30 ms / 句）——离线/内网/隐私优先。
>    - **OpenAI 兼容端点**——同一套 `IEmbeddingProvider` 接口，URL + Key + Model 即可切换；天然支持"国际版"（OpenAI / Azure OpenAI）和"国内版"（DashScope / 智谱 GLM / 月之暗面 Moonshot / DeepSeek / SiliconFlow / 火山方舟 等任何 OpenAI-compat 网关）。
>    - 配置 `SonnetDBServer__Copilot__Embedding__Provider = local|openai`，`Endpoint` / `ApiKey` / `Model` 三件套；**对话模型走同一抽象**，复用同一套 provider 切换逻辑。
> 4. **零破坏**：内容放在 ` SonnetDB.Copilot` 命名空间下， 不新增项目， 不污染 `SonnetDB.Core`；服务端默认启用。
> 5. **技能库 = 文件系统 + 前置语义召回**：`copilot/skills/*.md`（带 frontmatter `description` / `triggers`），第一轮根据用户问题做向量召回，命中后再加载到上下文。

### PR 列表

| PR | 主题 | 状态 |
|----|------|------|
| #63 | **`SonnetDB.Copilot` 命名空间骨架 + Embedding 抽象**（不新建项目，代码放入现有 `SonnetDB.Core` / `SonnetDB.Server`；引用 `Microsoft.Agents.AI` / `Microsoft.Extensions.AI` / `Microsoft.ML.OnnxRuntime`）；定义 `IEmbeddingProvider` / `IChatProvider` 抽象 + `LocalOnnxEmbeddingProvider`（bge-small-zh）+ `OpenAICompatibleEmbeddingProvider`（含 `OpenAICompatibleChatProvider`）；`SonnetDBServer__Copilot__*` 配置节 + DI 装配；`/healthz` 暴露 Copilot ready 标志；不接入任何业务流程 | 📋 |
| #64 | **文档摄入管线 + Knowledge 库**：新建 `Tsdb` 内嵌系统库 `__copilot__`，自动建表 `docs(time, source TAG, section TAG, title TAG, content STRING, embedding VECTOR(384))`；`DocsIngestor` 扫描 `docs/*.md` + `web/admin/help/`，按 H2/H3 切片（≤ 800 字 / 100 字 overlap）→ 嵌入 → 批量入库；CLI `sndb copilot ingest --root ./docs` 与服务端启动时自动增量同步（按文件 mtime 判定）；提供 MCP tool `docs_search(query, k)` | 📋 |
| #65 | **技能库 + 技能路由**：新增 `copilot/skills/*.md`（首批：`query-aggregation` / `pid-control-tuning` / `forecast-howto` / `troubleshoot-slow-query` / `schema-design` / `bulk-ingest`），frontmatter 含 `name` / `description` / `triggers` / `requires_tools`；`SkillRegistry` 启动时把每个技能 `description + triggers` 嵌入到 `__copilot__.skills`；新增 MCP tool `skill_search(query, k)` / `skill_load(name)`；技能加载后被 Agent 注入 system prompt | 📋 |
| #66 | **Schema 工具增强 + 抽样工具**：在现有 MCP 工具基础上补齐 `list_databases()` / `sample_rows(measurement, n=5)` / `explain_sql(sql)`（返回估算扫描段数 / 行数）；schema 工具结果加入 30s 内存缓存；所有新工具同样接入 `GrantsStore` 数据库级权限 | 📋 |
| #67 | **Agent Host：单轮问答闭环**：`CopilotAgent`（基于 Microsoft Agent Framework）= Embedding Provider + Chat Provider + MCP tools + Skills + Docs；HTTP 端点 `POST /v1/copilot/chat`（NDJSON 流式 SSE）+ `/v1/copilot/chat/stream`；最小回路：用户问题 → 召回 skills + docs → 选 tools → 执行 → 回答 + citations；Bearer 鉴权 + 数据库级 read 权限校验 | 📋 |
| #68 | **多轮 + 自我纠错 + Web Admin 集成**：Agent 支持多轮 history（按 token 预算裁剪）；SQL 执行失败时把 `SqlExecutionException` 反馈给模型让其改写（最多 3 轮）；`web/admin/` 新增 Chat Tab（Naive UI 流式渲染 + skill/citation 折叠展示 + 一键复制 SQL 到控制台执行） | 📋 |
| #69 | **Eval 套件 + 回归基准**：在现有 `tests/SonnetDB.Test` 下新增 `Copilot/` 目录，添加 30~50 个标准问答（schema 查询 / 聚合 / 时间过滤 / PID / forecast / 排错），用 `pytest-agent-evals` 风格的 .NET 实现：accuracy（SQL 等价/结果等价）、latency、citation 命中率三个指标；CI 中 nightly 运行（不阻塞主 CI）；README 新增"Copilot 能力矩阵"表 | 📋 |

### 配置预览

```jsonc
// appsettings.json
"SonnetDBServer": {
  "Copilot": {
    "Enabled": true,
    "Embedding": {
      "Provider": "local",                    // local | openai
      "LocalModelPath": "./models/bge-small-zh-v1.5-int8.onnx",
      "Endpoint": "https://api.openai.com/v1",
      "ApiKey": "${OPENAI_API_KEY}",
      "Model": "text-embedding-3-small"
    },
    "Chat": {
      "Provider": "openai",                   // openai
      "Endpoint": "https://dashscope.aliyuncs.com/compatible-mode/v1",
      "ApiKey": "${DASHSCOPE_API_KEY}",
      "Model": "qwen-max"
    },
    "Docs": { "AutoIngestOnStartup": true, "Roots": [ "./docs", "./web/admin/help" ] },
    "Skills": { "Root": "./copilot/skills" }
  }
}
```

### 推进顺序

PR #63（骨架 + Provider 抽象）→ #64（文档摄入）→ #65（技能库）→ #66（工具增强）→ #67（Agent 单轮）→ #68（多轮 + Web）→ #69（Eval）。
**前置依赖**：Milestone 13 的 PR #58 / #59 / #60 至少需要先合并，PR #64 才能在 SonnetDB 自身上落库。

---

## 里程碑总览

| Milestone | 主题 | PR 范围 | 状态 |
|-----------|------|---------|------|
| 0 | 项目脚手架 | #1 ~ #3 | ✅ |
| 1 | 内存与二进制基础设施 | #4 ~ #6 | ✅ |
| 2 | 逻辑模型与目录 | #7 ~ #9 | ✅ |
| 3 | 写入路径 | #10 ~ #13 | ✅ |
| 4 | 查询路径 | #14 ~ #16 | ✅ |
| 5 | 稳定性与性能（写入侧） | #17 ~ #21 | ✅ |
| 6 | SQL 前端 + Tag 倒排索引 | #22 ~ #28 | ✅ |
| 7 | 压缩编码（Delta / Gorilla） | #29 ~ #31 | ✅ |
| 8 | 服务器模式（HTTP + 远端 ADO + 控制面 + Vue3 后台 + SSE） | #32 ~ #34c | ✅ |
| 9 | 性能基准与发布 | #35 ~ #39（含 #36、#37a、#37b） | ✅ |
| 10 | 扩展和第三方 | #40, #41 + #42~#45 批量入库专题 | 🚧（#42~#45 ✅） |
| 11 | 写入快路径（PR #45 瓶颈收尾） | #46 ~ #49 | ✅ |
| 12 | 函数与算子扩展（PID / Forecast / UDF） | #50 ~ #57 | ✅ |
| 13 | 向量类型与嵌入式向量索引（Copilot 知识库底座） | #58 ~ #62 | 📋 |
| 14 | SonnetDB Copilot：MCP 工具 + 知识库 + 智能体 | #63 ~ #69 | 📋 |

**当前推进顺序**：Milestone 13（向量类型）→ Milestone 14（Copilot），其中 PR #58→#59→#60 是 Copilot 知识库的硬前置；PR #61（HNSW）/ #62（向量基准）允许与 Milestone 14 并行推进。

---

## 与原路线图的差异说明

1. **PR #15 重定义**：从原"QueryEngine.QueryRaw"改为"多段索引层（SegmentIndex / MultiSegmentIndex / SegmentManager）"；原 QueryRaw 内容并入新 PR #16。
2. **Milestone 5 重定义**：从原"SQL 前端"改为"稳定性与性能（写入侧）"，新增 PR #17（后台 Flush + Checkpoint replay 跳过） / #18（Compaction） / #19（多 WAL 滚动） / #20（DELETE/Tombstone） / #21（Retention TTL，待派单）。
3. **SQL 前端整体后移到 Milestone 6**，并扩充 Tag 倒排索引（PR #27）与 ADO.NET API（PR #28）。
4. **压缩编码独立为 Milestone 7**（原 Milestone 6 的 PR #22 / #23 / #24 中，Compaction 已在新 PR #18 完成；保留的 Delta / Gorilla 编码工作迁入此处）。
5. **单文件容器方案放弃**：当前多文件布局（`catalog.SDBCAT` + `wal/*.SDBWAL` + `segments/*.SDBSEG` + `tombstones.tslmanifest`）已稳定且对运维/备份/排错友好；单文件需新增 page manager + shadow paging，会重写 M3~M5 的崩溃恢复矩阵，收益不足以覆盖成本。原 M8 改为"服务器模式"。
6. **Milestone 8 重定义为服务器模式**：仅 HTTP（`POST /v1/db/{db}/sql` + ndjson 流式结果），WebSocket 推后评估；多租户采用进程内 `Tsdb` 注册表；权限仅 Bearer token + 三角色，不做 SQL 级 GRANT。
7. **执行顺序前置**：PR #21（Retention TTL）与 PR #35（嵌入式 Benchmark 基线）必须先于 M8 完成。
8. **发布顺延到 Milestone 9。**
9. **新增 Milestone 12 — 函数与算子扩展**：将 `enum Aggregator` 重构为 `FunctionRegistry` + `IAggregateFunction`，引入 Tier 1–5 共 ~50 个函数；以 **PID 与 Forecast** 作为内置差异化能力，并开放 UDF 注册 API 给嵌入式生态。该里程碑独立于原路线图，定位为 SonnetDB 在工业 / IoT / 可观测性场景的横向扩展层。
10. **新增 Milestone 13 — 向量类型与嵌入式向量索引**：引入 `VECTOR(dim)` 数据类型与 `cosine_distance` / `l2_distance` / `inner_product` 标量函数 + `knn(...)` 表值函数，第一版以 brute-force + SIMD 实现，HNSW 作为可选段内 sidecar 索引（`SDBVIDX`）。定位为 Milestone 14 Copilot 知识库的存储底座，同时让 SonnetDB 形成"时序 + 向量"二合一的对外差异化能力。距离计算全部走 `System.Numerics.Tensors.TensorPrimitives`，继续遵守 Safe-only 原则。
11. **新增 Milestone 14 — SonnetDB Copilot**：基于 Microsoft Agent Framework 的智能体层，复用现有 `/mcp/{db}` 工具集 + Milestone 13 的向量召回，把"用户文档 / 技能库 / 数据库 schema"全部 dogfood 到 `__copilot__` 系统库中。Embedding/Chat 走统一 `IEmbeddingProvider` / `IChatProvider` 抽象，**本地 ONNX（bge-small-zh）** 与 **OpenAI 兼容端点（国际版 / 国内版任意 OpenAI-compat 网关）** 同时支持，可按部署场景切换。新增项目 `src/SonnetDB.Copilot/`，与 `SonnetDB.Core` 解耦，服务端可选启用，不破坏现有功能。
