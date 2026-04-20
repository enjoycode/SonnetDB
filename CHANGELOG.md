# Changelog

本项目所有重要变更将记录在此文件中。
格式遵循 [Keep a Changelog 1.1.0](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [SemVer 2.0.0](https://semver.org/lang/zh-CN/)。

## [Unreleased]

### Added
- **TSLite.Server**：Native AOT 友好的 Minimal API HTTP 服务器（PR #32）
  - 新项目 `src/TSLite.Server/`，基于 `Microsoft.NET.Sdk.Web` + `WebApplication.CreateSlimBuilder` + `EnableRequestDelegateGenerator=true`，全程零反射，可 `dotnet publish -p:PublishAot=true` 产出单文件可执行（win-x64 ~11.5MB），AOT 警告数为 0。
  - 多租户：进程内 `TsdbRegistry`（`ConcurrentDictionary<string, Tsdb>`）+ `DataRoot/<db>/` 子目录隔离，启动时按需加载已存在数据库；`POST /v1/db`（admin）创建、`DELETE /v1/db/{db}`（admin）销毁、`GET /v1/db` 列表；数据库名校验通过 `[GeneratedRegex]` 源生成器。
  - SQL 端点：`POST /v1/db/{db}/sql`（单条）+ `POST /v1/db/{db}/sql/batch`（多条），结果以 `application/x-ndjson` 流式返回（meta 行 + 每行 JSON 数组 + end 行），通过手写 `Utf8JsonWriter` 避免多态序列化；其余 DTO 全部走 `System.Text.Json` 源生成器（`ServerJsonContext`）。
  - 认证：`Authorization: Bearer <token>` 三角色（`admin` / `readwrite` / `readonly`），自定义中间件直接读 `ServerOptions.Tokens` 静态映射，非 `/healthz` `/metrics` 一律强制鉴权；写操作（INSERT/DELETE/DDL）需 `readwrite` 或 `admin`，建删数据库需 `admin`。
  - 可观测性：`GET /healthz` 返回 JSON 健康摘要；`GET /metrics` 输出 Prometheus 文本格式（`tslite_uptime_seconds` / `tslite_databases` / `tslite_sql_requests_total` / `tslite_sql_errors_total` / `tslite_rows_inserted_total` / `tslite_rows_returned_total` / per-db `tslite_segments{db="..."}`）。
  - 6 个端到端集成测试（`tests/TSLite.Server.Tests/ServerEndToEndTests.cs`）覆盖 Healthz / Metrics 匿名访问、SQL 鉴权、admin 角色限定、CREATE→INSERT→SELECT→DROP 全链路、ndjson 解析、未知数据库 404。
- **整库 Native AOT 兼容**：`Directory.Build.props` 默认开启 `IsAotCompatible=true`（测试与基准项目显式关闭）；`TSLite` / `TSLite.Cli` / `TSLite.Server` 全部以零 IL/AOT 警告通过 `dotnet publish -p:PublishAot=true`。
  - `TsdbDataReader.GetFieldType` 重构：内部 `Type[]` 改为 `enum ColumnTypeKind`，并添加 `[DynamicallyAccessedMembers]` 标注 + `typeof(...)` 常量 switch，消除 IL2063/IL2093 警告，对外 API 与运行时行为完全保持。
- **CI**：`.github/workflows/ci.yml` 新增 `aot-publish` job（Linux + Windows 矩阵），执行 `dotnet publish -p:PublishAot=true /warnaserror` 验证 `TSLite.Cli` 与 `TSLite.Server`，并上传 publish 产物（PR #32）。

### Changed
- `InsertBenchmark`、`QueryBenchmark`、`AggregateBenchmark`：将内存占位实现替换为真实 `Tsdb` 引擎调用（PR #35）
- `README.md` 性能基准章节扩展为 **TSLite vs SQLite vs InfluxDB 2.7 vs TDengine 3.3.4** 四方对比（基于 1M 点数据集，单机容器）

### Fixed
- 基准测试 `GlobalCleanup` 中 SQLite 连接池文件锁问题（`SqliteConnection.ClearAllPools()`）（PR #35）
- `_influxAvailable` 现正确使用 `PingAsync()` 返回值而非无条件设为 `true`（PR #35）
- `InsertBenchmark.GlobalCleanup` 不再删除 InfluxDB bucket，避免后续 benchmark 进程的 `IterationSetup` 因 bucket 缺失而抛 `NotFoundException`
- `EnsureInfluxBucketAsync()`：三个 benchmark 在 `GlobalSetup` 中自动创建缺失的 `benchmarks` bucket
- TDengine SQL：`value` / `host` 列名加反引号绕开保留字解析错误，确保 4-DB 全部产出有效结果


### Added
- 新增段文件编码 / 字节统计快照 `SegmentReader.GetStats()`（PR #31）
  - 新增公开 record `TSLite.Storage.Segments.SegmentStats`（含 `BlockCount` / `TotalPointCount` / `TotalFieldNameBytes` / `TotalTimestampPayloadBytes` / `TotalValuePayloadBytes` / `RawTimestampBlocks` / `DeltaTimestampBlocks` / `RawValueBlocks` / `DeltaValueBlocks` / `ByFieldType` 以及计算型属性 `AverageTimestampBytesPerPoint` / `AverageValueBytesPerPoint`）与 `FieldTypeStats`（`BlockCount` / `PointCount` / `ValuePayloadBytes` / `DeltaValueBlocks`），为运维巡检、压缩率对比、基准测试提供结构化输出。
  - `SegmentReader.GetStats()`：按需遍历 `BlockDescriptor[]`，一次迭代同时计算总量 / 按 `BlockEncoding` 拆分 / 按 `FieldType` 分组三个维度；不缓存。可用于对同一 `MemTable` 分别以 V1 / V2 写入后对比 `Total*PayloadBytes` 验证压缩效果。
  - `SegmentStats.ByFieldType` 使用 `IReadOnlyDictionary<FieldType, FieldTypeStats>` 提供面向查询，默认为空字典以避免空段访问 NRE。
  - 6 个新测试（`SegmentReaderStatsTests`）覆盖：默认 V1 全部计入 raw 且字节数符合 8B/点；单独开启 V2 时间戳验证只选取时间戳压缩、值字节数不变；单独开启 V2 值（String 字典）验证值字节压缩、时间戳保持 V1；双 V2两个计数器都增加、平均字节/点均 < 8；多 `FieldType` 混合段按组计数与点数一致；空 `SegmentStats` 除零防护。

- 新增数值列 V2 编码：Float64 Gorilla XOR + Boolean RLE + String 字典（PR #30）
  - 新增内部位流工具 `TSLite.Storage.Segments.BitIo`：`BitWriter` / `BitReader` ref struct，高位优先按位写读，最大 64 位/调用。
  - 新增内部值列 V2 编解码器 `TSLite.Storage.Segments.ValuePayloadCodecV2`：
    - **Float64**：简化版 Gorilla XOR — 第一个值 64 位锚点，之后每点 1 位控制位；变化点再写 6 位 leadingZeros + 6 位 (meaningful-1) + meaningful 位有效位。常量序列压缩到 ≈1 位/点。
    - **Boolean**：游程长度编码（RLE）— 1 字节初值 + 交替 varint 段长。
    - **String**：按出现顺序构建字典 — `varint(dictSize)` + `dictSize × (varint(byteLen) + UTF-8)` + `count × varint(idx)`，重复值高度压缩。
    - **Int64**：本 PR 暂不压缩，仍为 8B LE 直存（与 V1 等价）。
  - `SegmentWriterOptions.ValueEncoding`：默认 `None`（V1）以保证已有段文件与测试行为不变；显式设为 `DeltaValue` 启用 V2 并在 `BlockHeader.Encoding` 与 `TimestampEncoding` 标志位独立组合。
  - `BlockDecoder.ReadValues` / `ReadValuesRange` 新增基于 `descriptor.ValueEncoding` 的 V1/V2 分发；V2 范围读取需先全量解码再切片（XOR/RLE/字典本质顺序）。
  - 19 个新测试（`ValuePayloadCodecV2Tests`）覆盖：Float64 空/单点/常量序列压缩/递增序列/特殊值（NaN/±Inf/±0）round-trip；Bool 全 true/交替/混合 run/损坏 run 越界；String 全相同/含 unicode/含空串/字典索引越界；Int64 V2 透传；SegmentWriter 默认无标志、单独 `DeltaValue`（Float64/Bool/String 均显著小于 V1）、`DeltaTimestamp | DeltaValue` 双标志组合及 `DecodeBlockRange` 与 V1 一致。

- 新增时间戳 Delta-of-Delta + ZigZag varint 编码（V2 block payload，向后兼容 V1）（PR #29）
  - 新增内部 `TSLite.Storage.Segments.TimestampCodec`：`MeasureDeltaOfDelta` / `WriteDeltaOfDelta` / `ReadDeltaOfDelta`。V2 格式：8 字节定点锐 + 1 个一阶差分 + 剩余二阶差分，常规采样间隔下压缩到 ≈1 字节/点。
  - `BlockEncoding` 改为 `[Flags]`：`DeltaTimestamp` (1) 与 `DeltaValue` (2) 可独立开关；`SegmentReader` 根据 bit 拆分到 `BlockDescriptor.{TimestampEncoding, ValueEncoding}`。
  - `SegmentWriterOptions.TimestampEncoding`：默认 `None`（V1）以保证已有文件与测试行为不变；显式设为 `DeltaTimestamp` 则启用 V2 并在 `BlockHeader.Encoding` 中置位。
  - `BlockDecoder` 联合读取路径（全量与范围）根据 `descriptor.TimestampEncoding` 分发；V2 路径需要完整重现时间戳后才能二分，已与现有范围查询逻辑保持一致。
  - 13 个新测试（`TimestampCodecTests`）覆盖：空序列、单点、规则间隔压缩占比、不规则间隔、负二阶差分、大锐点、buffer 长度不匹配、锐点截断、varint 越界、SegmentWriter 默认 V1、V1↔V2 跳点一致、`DecodeRange` 一致、`BlockDescriptor` 标志保留。

- 新增标准 ADO.NET API，提供 `TsdbConnection` / `TsdbCommand` / `TsdbDataReader` / `TsdbParameter` / `TsdbParameterCollection` / `TsdbConnectionStringBuilder`（PR #28）
  - `TSLite.Ado.TsdbConnection : System.Data.Common.DbConnection`：连接字符串为 `Data Source=<根目录>`（大小写不敏感，由 `DbConnectionStringBuilder` 提供）；同进程同路径多次 `Open` 通过内部 `SharedTsdbRegistry` 引用计数共享同一 `Tsdb`，避免 WAL 锁冲突；事务与 `ChangeDatabase` 抛 `NotSupportedException`
  - `TSLite.Ado.TsdbCommand : DbCommand`：包装 `SqlExecutor`；`ExecuteNonQuery` 返回 INSERT 写入行数 / DELETE 増加的墓碑总数 / CREATE MEASUREMENT 0 / SELECT -1；`ExecuteScalar` 返回 SELECT 首行首列（空集返 null）；`ExecuteReader` 包装 `SelectExecutionResult`，非 SELECT 语句返回零行 reader 并携带 `RecordsAffected`
  - 参数绑定：支持 `@name` 与 `:name` 占位符，执行前以状态机扫描 SQL 文本并跳过字符串字面量 / 双引号标识符 / 行注释；支持类型包括 `string` / `bool` / 整型 / 浮点 / `decimal` / `DateTime` / `DateTimeOffset`（后两者转为 Unix 毫秒）/ `null` / `DBNull`；字符串值会被单引号包裹并把内部 `'` 转义为 `''`，避免 SQL 注入
  - `TsdbDataReader : DbDataReader`：完整实现 `Read` / `GetXxx` / `IsDBNull` / `GetOrdinal` / `GetFieldType`（以首个非 null 行推断）/ `HasRows` / `RecordsAffected` / `CommandBehavior.CloseConnection`。`NextResult` 总为 `false`，`GetBytes` / `GetChars` 抛 `NotSupported`
  - 单元测试：31 个端到端测试（`TsdbAdoApiTests`）覆盖连接生命周期 / 共享 `Tsdb` / `BeginTransaction` 不支持 / `ConnectionStringBuilder` 大小写不敏感 / 三种 `ExecuteXxx` / 参数状态机（跳过字面量与标识符）/ 参数转义防注入（`O'Brien` 场景）/ 缺失参数报错 / `:name` 形式 / NULL 参数 / 多个 CommandText 错误路径 / `CloseConnection` 行为

- 新增 Tag 倒排索引以加速 `SELECT/DELETE` 的 `WHERE tag = '...'` 过滤（PR #27）
  - `TSLite.Catalog.TagInvertedIndex`（internal）：维护 `measurement → SeriesId 集合` 与 `measurement → tagKey → tagValue → SeriesId 集合` 两级映射；全部使用 `ConcurrentDictionary` 实现单写多读线程安全；集合本身用 `ConcurrentDictionary<ulong, byte>` 模拟并发集合
  - `TSLite.Catalog.SeriesCatalog.Find(measurement, tagFilter)`：从全表线性扫描改为基于倒排索引的候选集交集（基准选最小集合，规模上界为 `min(|S_i|)`）；返回前仍执行一次防御性 measurement+tag 重校验以容忍倒排索引与 `_byCanonical` 的瞬间不一致
  - 索引在 `GetOrAddInternal` 中仅由胜出的 `candidate` 线程写入（`ReferenceEquals(entry, candidate)`），并在 `LoadEntry`（`CatalogFileCodec` 重放路径）与 `Clear` 中维护——索引本身不进入持久化格式，启动时由现有持久化条目重建，因此**未变更磁盘 catalog 文件格式**
  - 单元测试：11 个新增测试（`TagInvertedIndexTests`）覆盖无 tag 过滤 / 单 tag / 多 tag 交集 / 未命中值 / 缺失 tagKey / 未知 measurement / measurement 隔离 / `Clear` 后清空 / 重复 `GetOrAdd` 索引不膨胀 / `LoadEntry` 重建 / 并发写读

- 新增 SQL `DELETE FROM ... WHERE ...` 执行支持（PR #26）
  - `TSLite.Sql.Execution.DeleteExecutionResult`（record，含 `Measurement` / `SeriesAffected` / `TombstonesAdded`）
  - `TSLite.Sql.Execution.DeleteExecutor`（internal）：复用 `WhereClauseDecomposer` 解析 tag 等值过滤 + 时间窗，对所有命中 tag 过滤的 series × schema 中所有 Field 列调用 `Tsdb.Delete(seriesId, fieldName, from, to)`，落到 PR #20 的 Tombstone 体系（WAL 追加 + 内存墓碑表 + 查询时过滤）
  - `SqlExecutor.ExecuteDelete(Tsdb, DeleteStatement)` 公共入口；`Execute` 派发新增 `DeleteStatement` 分支
  - 语义：`WHERE host = 'h1' AND time >= a AND time <= b` 等价于命中 series 的所有 field 列在 `[a, b]` 闭区间打墓碑；省略 time 比较则覆盖全时间轴；省略 tag 过滤则作用于该 measurement 下所有 series；命中 0 series 直接返回零计数（不抛错）
  - 校验规则：measurement 必须存在；WHERE 与 SELECT 共用同一套约束（仅 AND、tag 等值、time 比较、不支持 OR/NOT/field 过滤）；空时间窗抛 `InvalidOperationException`
  - 单元测试：13 个端到端测试覆盖时间窗 + tag 过滤 / 仅时间窗 / 仅 tag 过滤 / `time = X` 单点删除 / 命中 0 series / 跨重启持久化（WAL replay）/ 删除后聚合验证 / 各类错误场景（缺 measurement / OR / field 过滤 / 未知 tag 列 / 空时间窗 / null 参数）

- 新增 SQL `SELECT ... [WHERE ...] [GROUP BY time(...)]` 执行支持（PR #25）
  - `TSLite.Sql.Execution.SelectExecutionResult`（record，含 `Columns` / `Rows`；行内运行时类型：time→`long`、tag→`string?`、field→`double/long/bool/string?`、count→`long`、其他聚合→`double`）
  - `TSLite.Sql.Execution.WhereClauseDecomposer`（internal）：将 WHERE AST 拆分为 `(TagFilter, TimeRange)`；仅支持顶层 `AND` 合取、`tag = 'literal'` 等值过滤、`time {= != >= > <= <}` 时间窗（`time !=` 暂不支持）；OR / NOT / field 过滤 / 非字面量右值 / 同 tag 列冲突值均抛 `InvalidOperationException`；自动检测空时间窗
  - `TSLite.Sql.Execution.SelectExecutor`（internal）：投影分类（time/tag/field/aggregate）；原始模式按 series 做时间戳并集 outer-join，缺失字段输出 `null`；聚合模式以 `SortedDictionary<long, BucketState[]>` 按桶累积 count/sum/min/max/first/last，`GROUP BY time(d)` 由 `TimeBucket.Floor` 对齐，无 GROUP BY 则全局单桶；多 series 的 sum/avg/min/max/count 自动跨 series 合并；`count(*)` 跨 schema 全部数值 field 求总点数（跳过 String）；`count(field)` 计数任意类型；其他聚合拒绝 String field
  - `SqlExecutor.ExecuteSelect(Tsdb, SelectStatement)` 公共入口；`Execute` 派发新增 `SelectStatement` 分支
  - 校验规则：聚合不可与裸列混用；`GROUP BY time(...)` 仅在聚合中有效；`first`/`last` 多 series 暂不支持（v1）；未知函数 / 未知列 / 聚合函数作用于 Tag 列均抛错
  - 单元测试：25 个端到端测试覆盖 `SELECT *` / 列投影 / outer-join NULL / WHERE 时间窗 / WHERE tag 过滤 / 别名 / `count(*)` / `count(field)` / `sum/avg/min/max` / `first/last` / 多 series 聚合 / `GROUP BY time(1000ms)` / 空时间窗 / 各类错误场景（缺 measurement / 未知列 / OR / field 过滤 / 混合投影 / 缺聚合的 GROUP BY / first 多 series / tag 不等 / tag 冲突 / String 字段 sum）

- 新增 SQL `INSERT INTO ... VALUES (...)` 执行支持（PR #24）
  - `TSLite.Sql.Execution.InsertExecutionResult`（record，含 `Measurement` / `RowsInserted`）
  - `SqlExecutor.ExecuteInsert(Tsdb, InsertStatement)`：完整列绑定 + 类型校验 + 时间戳缺省 + 批量写入
  - 校验规则：measurement 必须已 CREATE；列名必须存在于 schema；同一 INSERT 列列表禁止重复；Tag 必须为字符串字面量且非 NULL；Field 类型必须匹配（INT 字面量可隐式提升为 FLOAT）；每行至少 1 个 Field 列值；`time` 列必须为非负整数字面量；缺省时使用 `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()`
  - `SqlParser`：新增内部 `ExpectColumnName()`，允许 INSERT 列列表中将保留字 `time` 作为列名使用（与时间戳伪列对应；亦可继续用 `"time"` 引号转义）
  - `SqlExecutor.ExecuteStatement` 现支持 `InsertStatement` 派发
  - 单元测试：17 个端到端测试，覆盖单行 / 批量 / 时间缺省 / 时间大小写不敏感 / Int→Float 提升 / 全四种 FieldType round-trip / 仅 Field 无 Tag / measurement 缺失 / 未知列 / 重复列 / 类型不匹配 / Tag 非字符串 / NULL / 缺 Field / 负时间戳 / 批量部分失败前序已落地 / 参数 null

- 新增 `TSLite.Catalog` 命名空间下 measurement schema 体系，并接入 `Tsdb` 与 SQL 执行器（PR #23）
  - `MeasurementColumnRole`（Tag/Field 角色枚举）、`MeasurementColumn`（列定义 record）、`MeasurementSchema`（不可变值对象，工厂 `Create` 校验：列非空、≥1 个 Field、列名唯一、Tag 列必须 STRING、禁止 Unknown 类型）、`MeasurementCatalog`（基于 `ConcurrentDictionary` 的线程安全注册表）
  - `MeasurementSchemaCodec`：新增持久化文件 `measurements.tslschema`；二进制格式 `Magic(8) + FormatVersion(4) + HeaderSize(4) + Count(4) + Reserved(12)` 头 + 变长 measurement 记录 + `Crc32(4) + Magic(8) + Reserved(4)` 尾；`ArrayPool<byte>` + `SpanReader` / `SpanWriter` 实现，`Save` 走临时文件 + 原子 rename + fsync
  - `TsdbPaths.MeasurementSchemaFileName` / `MeasurementSchemaPath(root)` 路径常量
  - `Tsdb.Measurements` 属性 + `Tsdb.CreateMeasurement(MeasurementSchema)`：注册到 catalog 并立刻把全量 schema 集合原子持久化（崩溃安全）；`Open` 启动时加载、`Dispose` 关闭时再次保存
  - 新增 `TSLite.Sql.Execution.SqlExecutor`：`Execute(Tsdb, sql)` / `ExecuteStatement(Tsdb, SqlStatement)` / `ExecuteCreateMeasurement(Tsdb, CreateMeasurementStatement)`；把 AST `ColumnDefinition` 映射到 catalog `MeasurementColumn` 后调用 `Tsdb.CreateMeasurement`；其余语句类型暂抛 `NotSupportedException` 留待后续 PR
  - 单元测试：8 个 schema 校验 + 5 个 codec round-trip / 损坏检测 + 5 个执行器与持久化端到端测试

- 新增 `TSLite.Sql` 命名空间：纯 Safe-only、零第三方依赖的 SQL 词法 + 语法分析器（PR #22）
  - `TokenKind` / `Token` / `SqlLexer`：单遍词法分析；关键字大小写不敏感；标识符保留原始大小写；支持单引号字符串字面量（`''` 转义）、双引号引用标识符（`""` 转义）、整数/浮点字面量、duration 字面量（`ns / us / ms / s / m / h / d`，统一归一化为毫秒）、`-- 行注释`、`/* 块注释 */`、运算符 `= != <> < <= > >= + - * / %`
  - `Sql.Ast`：AST 节点（`SqlStatement` / `CreateMeasurementStatement` / `InsertStatement` / `SelectStatement` / `DeleteStatement` / `ColumnDefinition` / `SelectItem` / `TimeBucketSpec` / `SqlExpression` 派生：`LiteralExpression` / `DurationLiteralExpression` / `IdentifierExpression` / `StarExpression` / `FunctionCallExpression` / `BinaryExpression` / `UnaryExpression`），均为 `record` 值语义
  - `SqlParser`：递归下降解析器，覆盖 `CREATE MEASUREMENT` / `INSERT INTO ... VALUES (...) [, (...)]*` / `SELECT projections FROM measurement [WHERE ...] [GROUP BY time(duration)]` / `DELETE FROM measurement WHERE ...`；支持 `*` 通配、聚合函数（`count(*) / avg(x) / ...`）、`AS alias` 与裸 alias、`AND / OR / NOT` 短路逻辑、6 种比较与 5 种算术运算、括号显式优先级、`NULL / TRUE / FALSE` 字面量；新增 `SqlParser.Parse(string)` 解析单语句、`SqlParser.ParseScript(string)` 解析多语句脚本（分号分隔）
  - 关键字 `time` 在表达式中既可作为列名（`time >= 100`）也可作为函数（`time(1m)`），通过下一个 token 是否为 `(` 自动消歧
  - `SqlParseException`：携带源 SQL 字符位置的诊断异常
  - 单元测试：50 个 Lexer + Parser 测试，覆盖关键字大小写、字符串/标识符/duration 转义、运算符优先级、注释跳过、错误位置等

- 新增 `TSLite.Engine.Retention.RetentionPolicy`：数据保留策略；支持全局 TTL、轮询周期、限流（MaxTombstonesPerRound）及虚拟时钟注入（NowFn）（PR #21）
- 新增 `TSLite.Engine.Retention.RetentionPlan` / `TombstoneToInject`：单次 Retention 扫描的产物（纯计算，无副作用）
- 新增 `TSLite.Engine.Retention.RetentionPlanner`：从当前段集合产出 `RetentionPlan` 的纯函数；支持整段 drop、部分过期墓碑注入、已有等价墓碑去重及限流截断
- 新增 `TSLite.Engine.Retention.RetentionWorker`：后台 Retention 工作线程，双路径回收——整段直接 drop（MaxTimestamp < cutoff） + 墓碑注入（部分过期段，由 Compaction 在下一轮物理删除）
- 新增 `TSLite.Engine.Retention.RetentionExecutionStats`：单次 Retention 扫描统计（Cutoff / DroppedSegments / InjectedTombstones / ElapsedMicros）
- `SegmentManager.DropSegments(IReadOnlyList<long>)`：原子移除多个段，重建索引快照，Dispose 旧 reader，返回被移除列表（PR #21）
- `Tsdb.Retention`：暴露后台 Retention 工作线程（仅当 `TsdbOptions.Retention.Enabled=true` 时非 null）
- `TsdbOptions.Retention`：Retention TTL 策略入口（默认禁用，保持向后兼容）
- `RetentionPolicy.NowFn` 支持注入虚拟时钟（测试 + 自定义时间戳单位）

**Milestone 5 完成**（PR #17 后台 Flush + #18 Compaction + #19 多 WAL 滚动 + #20 DELETE-Tombstone + #21 Retention TTL）。

- 删除支持：`Tsdb.Delete(seriesId, field, from, to)` 和 `Tsdb.Delete(measurement, tags, field, from, to)`，返回操作是否成功（PR #20）
- WAL 新增 `RecordType.Delete = 5`（向后兼容），`WalWriter.AppendDelete` / `WalSegmentSet.AppendDelete` 追加删除记录
- 新增 `Tombstone`（readonly record struct）：墓碑数据结构，声明 (SeriesId, FieldName) 在时间窗 [From, To] 内的数据已被永久标记删除（v1 时间窗语义，无 perPoint LSN 比对）
- 新增 `TombstoneTable`：进程内墓碑集合，按 (SeriesId, FieldName) 索引；线程安全（lock 写 + Volatile 读快照）；提供 `IsCovered` / `GetForSeriesField` / `Add` / `LoadFrom` / `RemoveAll`
- 新增 `TombstoneManifestCodec`（`TSLite.Wal`）：墓碑清单文件 `<root>/tombstones.tslmanifest` 的序列化与反序列化；包含 Magic / FormatVersion / Crc32 校验；临时文件 + 原子 rename 写入
- 新增 `TsdbPaths.TombstoneManifestPath`：返回清单文件完整路径
- `Tsdb.Tombstones` 属性：暴露进程内墓碑集合
- 查询路径自动应用墓碑：`QueryEngine` 的 `Execute(PointQuery)` 和 `Execute(AggregateQuery)` 均一致过滤被墓碑覆盖的数据点（后者通过复用 PointQuery 路径自动获得）
- `SegmentCompactor.Execute` 新增可选 `TombstoneTable?` 参数：Compaction 时物理删除被墓碑覆盖的数据点；若某 (SeriesId, FieldName) 全部点被覆盖，则不生成对应 Block
- `CompactionWorker`：Swap 完成后自动回收"不再覆盖任何活段"的墓碑，更新 `TombstoneTable` 并重写 manifest
- 崩溃恢复：`Tsdb.Open` 启动时加载 manifest，再从 WAL replay 追加 CheckpointLsn 之后的 Delete 记录，最后重写 manifest（双路恢复）

### Changed
- `FlushCoordinator.Flush` 新增可选 `TombstoneTable?` 参数；Flush 序列在 WriteSegment 之前插入第 0 步：持久化 tombstone manifest（确保 WAL recycle 后墓碑不丢失）
- `WalReplayResult` 新增 `IReadOnlyList<DeleteRecord> DeleteRecords` 字段（含 CheckpointLsn 之后的删除记录）
- `WalReader.Replay()` 和 `WalSegmentSet.ReplayWithCheckpoint` 新增对 `WalRecordType.Delete` 的解析，产出 `DeleteRecord`
- `Tsdb.Dispose`：若 MemTable 为空（无需 Flush），仍会持久化 tombstone manifest


- 新增 `TSLite.Wal.WalSegmentSet`：多 WAL segment 管理器（Append / Sync / Roll / RecycleUpTo / ReplayWithCheckpoint），支持多 segment 滚动写入与按 CheckpointLsn 整段回收（PR #19）
- 新增 `WalSegmentLayout`（static）：WAL segment 文件命名约定（`{startLsn:X16}.tslwal`）、枚举、`TryParseStartLsn` 及 legacy `active.tslwal` 升级工具
- 新增 `WalSegmentInfo`（readonly record struct）：segment 元数据（StartLsn / Path / FileLength）
- 新增 `WalRollingPolicy`：WAL 滚动策略配置（Enabled / MaxBytesPerSegment=64MB / MaxRecordsPerSegment=1M 双阈值）
- 新增 `Tsdb` 启动时的 legacy `wal/active.tslwal` 自动升级路径（`UpgradeLegacyIfPresent`）

### Changed
- `FlushCoordinator.Flush` 改为通过 `WalSegmentSet` 工作；Flush 顺序升级为：WriteSegment → AppendCheckpoint+Sync → Roll → RecycleUpTo(checkpointRecordLsn) → MemTable.Reset（PR #19）
- `Tsdb` 内部 `_walWriter` 替换为 `WalSegmentSet _walSet`；`Tsdb.Open` 现在调用 `WalSegmentSet.Open`（自动升级 legacy WAL）和 `WalSegmentSet.ReplayWithCheckpoint`
- `TsdbOptions` 新增 `WalRollingPolicy WalRolling` 属性（默认 `WalRollingPolicy.Default`）
- `WalTruncator.SwapAndTruncate` 标记 `[Obsolete]`，内部保留以兼容外部使用；替代方案：`WalSegmentSet.Roll + RecycleUpTo`


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
