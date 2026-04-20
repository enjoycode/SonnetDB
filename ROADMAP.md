# ROADMAP

本文件描述 TSLite 的分批 PR 开发计划，按 Milestone 组织。每个 PR 均包含：变更点、新增文件、测试覆盖与验收标准。

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
| #12 | `SegmentWriter` / `SegmentBuildResult` / `ValuePayloadCodec`（不可变 `.tslseg`：临时文件 + 原子 rename） | ✅ |
| #13 | `Tsdb` 引擎门面 + `FlushCoordinator` + `WalTruncator` + `TsdbPaths`（写入路径闭环；崩溃恢复矩阵齐全） | ✅ |

磁盘布局（M3 落地）：
```
<root>/
  catalog.tslcat
  wal/active.tslwal
  segments/{id:X16}.tslseg
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
| #19 | 多 WAL 滚动（segmented WAL）：`WalSegmentSet` / `WalRollingPolicy` + Legacy `active.tslwal` 自动升级 | ✅ |
| #20 | 删除支持（DELETE / Tombstone + WAL Delete 记录 + `tombstones.tslmanifest` + Compaction 阶段消化） | ✅ |
| #21 | Retention TTL：按策略自动注入墓碑 + 过期段直接 drop | ✅ |

磁盘布局（M5 落地后）：
```
<root>/
  catalog.tslcat
  tombstones.tslmanifest
  wal/{startLsn:X16}.tslwal             # 多 segment 滚动
  segments/{id:X16}.tslseg
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
| #28 | 按照标准的ADO.NET  API, 实现`TsdbConnection / TsdbCommand / TsdbDataReader`等等。 | ✅ |

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

- **运行时形态**：AOT-friendly Minimal API（`net10.0` + `PublishAot=true`），单进程；项目位于 `src/TSLite.Server/`，引用 `TSLite`。
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
| #32 | `TSLite.Server`：AOT Minimal API + 多 `Tsdb` 实例注册表 + `POST /v1/db/{db}/sql` + ndjson 流式结果 + Bearer token 三角色认证 + `/healthz` + `/metrics` | ✅ |
| #33 | 远端 ADO.NET 客户端 `TSLite.Data`：与 PR #28 共享 `TsdbConnectionStringBuilder`，通过 scheme（`tslite://` 本地、`tslite+http://` 远程）切换实现，结果集流式反序列化 | ✅ |
| #34a | 服务端控制面：用户/权限存储 + SQL DDL（CREATE/ALTER/DROP USER、GRANT/REVOKE、CREATE/DROP DATABASE）+ `POST /v1/auth/login` 颁发动态 token + Bearer 中间件接入 `UserStore` | ✅ |
| #34b | Vue3 管理后台（Naive UI）：登录页、数据库列表/状态、SQL 控制台、用户/权限/Token 管理 | ✅ |
| #34c | 实时推送：基于 SSE 的指标 / 慢查询 / 数据库事件流，前端订阅自动刷新 | ✅ |


---

## Milestone 9 — 性能与发布

| PR | 主题 | 状态 |
|----|------|------|
| #35 | BenchmarkDotNet：写入 / 查询 / 聚合 / Compaction 基准，编写评测 InfluxDB TDengine SQLite TSLite  TSLite.Server 等五个时序数据库的各项指标对比， 并连同机器性能都写在readme.md 里面。  | ✅|
| #37 | 文档完善： `docs/getting-started.md` / `docs/data-model.md` / `docs/sql-reference.md` / `docs/file-format.md` 使用 jekllynet 构建github pages 文档， 编写readme.md ， 纠正过时的文件说明，把现在的功能和架构重新书写、 重新整理一遍， 核对路线图， 核对代码和功能是否一致，我们明确废弃的就删掉， 有遗漏的我们评估是否要补齐。  | 📋 |
| #38 | 发布 NuGet 包 `TSLite 0.1.0` + `.github/workflows/publish.yml` ， 打包生唱成一套包含 tslite 和 tslite.data tslite.cil 的版本，并包含他们的使用说明， 发布linux和 windows版本， 再打包 TSLite.Server  一套编写版， 包含全部内容，包含前端，TSLite.cil tslite.data 等  能够一键启动，不需要额外设置，即可启动。 windows和linux分别打包， 再打包 windows 和linux的可安装的 msi和 dep rpm的版本。 | 📋 |
| #39 |  docker 服务端模式的镜像到 maikebing iotsharp组织， 编写ci 等能自动发布| 📋 |
---
## Milestone 10 — 扩展和第三方
| #40 |  实现cil的能力， 能够操作和打开本地文件， 可以连接tslite.server ， 评估是否考虑使用 TSLite.Data 来实现，或者是直接调用接口实现。 | 📋 |
| #41 |  TSLite.Server 支持 订阅MQTT消息，通过后台管理来添加订阅。   | 📋 |
| #42 | 批量入库快路径核心库 `TSLite.Ingest`：协议嗅探（Detector）+ 三协议 reader（LineProtocol / JSON / Bulk INSERT VALUES）+ `BulkIngestor` 统一消费入口（ArrayPool 8192 批 → `Tsdb.WriteMany`，支持 FailFast/Skip 与可选 FlushOnComplete）。绕开每条 INSERT 的 SQL Lexer→Parser→Planner 开销，为大批量写入提供基础；`src/TSLite` 仍保持零第三方运行时依赖。 | ✅ |
| #43 | `TSLite.Data` 接入：`TsdbCommand.CommandType = CommandType.TableDirect` 走批量入库快路径；`IConnectionImpl.ExecuteBulk` + `EmbeddedConnectionImpl` 桥接 `Tsdb.Measurements` 的 schema 到 `BulkValuesReader` 的列角色 resolver；嵌入式连接零拷贝直达 `BulkIngestor`。 | ✅ |
| #44 | `TSLite.Server` 远程批量端点：`POST /v1/db/{db}/measurements/{m}/lp\|json\|bulk` 三个端点 + `RemoteConnectionImpl.ExecuteBulk`；保留 SQL 路径不变。 | ✅ |
| #45 | 批量入库基准：在 `TSLite.Benchmarks` 新增 `BulkIngestBenchmark`，对比 SQL INSERT 单点 / TableDirect LP / TableDirect JSON / TableDirect Bulk VALUES，刷新 README 写入吞吐对比表。 | ✅ |

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
| #49 | **基准刷新 + 对外对比**：
 - ✅ README 「写入：100 万点」表新增 PR #47 服务端 LP/JSON/Bulk 三行（1.10–1.20 s / 34–71 MB / ~1.77–1.93× vs 嵌入式）；
 - ✅ README 「嵌入式 vs TSLite.Server」同机对比表拆分为 SQL Batch + LP/JSON/Bulk 四行；
 - ✅ README 「批量入库快路径」补充 PR #48 `?flush=` 三档位表（None / Async / Sync 语义与适用场景）；
 - ⏳ 重跑 `BulkIngestBenchmark` / `ServerInsertBenchmark` 含 PR #46~#48 后果（当前 README 采用 PR #47 实测数据，全量重跑为后续事项）；
 - ⏳ TDengine schemaless LP 采集路径接入（`InsertBenchmark.TDengine_Insert_1M` 目前为 REST INSERT，后续可额外加 schemaless 变体对照）。 | 🚧 |

**推进顺序**：PR #46 ✅ → PR #47 ✅ → PR #48 ✅ → PR #49。PR #47 已让服务端三端点进入「秒级 1M 点 + ≤ 80MB 分配」区间，超额完成 Milestone 11 ≥ 700k pts/s 的目标。PR #48 引入 `?flush=false|true|async` 三档位 + 新 `Tsdb.SignalFlush()` API，进一步降低批量入库 RTT；下一步 PR #49 做对外（InfluxDB / TDengine）对比刷新。




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
| 9 | 性能基准与发布 | #35 ~ #39 | 🚧（#35 已完成） |
| 10 | 扩展和第三方 | #40, #41 + #42~#45 批量入库专题 | 🚧（#42~#45 ✅） |
| 11 | 写入快路径（PR #45 瓶颈收尾） | #46 ~ #49 | 🚧 #46 ✅ #47 ✅ #48 ✅ #49 🚧（文档完成、全量重跑待定） |

**当前推进顺序**：PR #49（写入快路径专题剩余）→ PR #37 → PR #38 → PR #39。

---

## 与原路线图的差异说明

1. **PR #15 重定义**：从原"QueryEngine.QueryRaw"改为"多段索引层（SegmentIndex / MultiSegmentIndex / SegmentManager）"；原 QueryRaw 内容并入新 PR #16。
2. **Milestone 5 重定义**：从原"SQL 前端"改为"稳定性与性能（写入侧）"，新增 PR #17（后台 Flush + Checkpoint replay 跳过） / #18（Compaction） / #19（多 WAL 滚动） / #20（DELETE/Tombstone） / #21（Retention TTL，待派单）。
3. **SQL 前端整体后移到 Milestone 6**，并扩充 Tag 倒排索引（PR #27）与 ADO.NET API（PR #28）。
4. **压缩编码独立为 Milestone 7**（原 Milestone 6 的 PR #22 / #23 / #24 中，Compaction 已在新 PR #18 完成；保留的 Delta / Gorilla 编码工作迁入此处）。
5. **单文件容器方案放弃**：当前多文件布局（`catalog.tslcat` + `wal/*.tslwal` + `segments/*.tslseg` + `tombstones.tslmanifest`）已稳定且对运维/备份/排错友好；单文件需新增 page manager + shadow paging，会重写 M3~M5 的崩溃恢复矩阵，收益不足以覆盖成本。原 M8 改为"服务器模式"。
6. **Milestone 8 重定义为服务器模式**：仅 HTTP（`POST /v1/db/{db}/sql` + ndjson 流式结果），WebSocket 推后评估；多租户采用进程内 `Tsdb` 注册表；权限仅 Bearer token + 三角色，不做 SQL 级 GRANT。
7. **执行顺序前置**：PR #21（Retention TTL）与 PR #35（嵌入式 Benchmark 基线）必须先于 M8 完成。
8. **发布顺延到 Milestone 9。**
