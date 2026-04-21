# Changelog

本项目所有重要变更将记录在此文件中。
格式遵循 [Keep a Changelog 1.1.0](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [SemVer 2.0.0](https://semver.org/lang/zh-CN/)。

## [Unreleased]

### Changed (品牌重命名 / Breaking)
- **项目重命名 `TSLite` → `SonnetDB`**：因与其他品牌名称冲突，全仓代码、命名空间、包名、Docker 镜像、文档、CI 脚本统一改名为 `SonnetDB`。
  - **NuGet 包 ID**：
    - `TSLite` → `SonnetDB.Core`（核心嵌入式引擎库）
    - `TSLite.Server` → `SonnetDB`（HTTP 服务端 / Docker 镜像主品牌）
    - `TSLite.Cli` → `SonnetDB.Cli`（dotnet tool）
    - `TSLite.Data` → `SonnetDB.Data`（ADO.NET 提供程序）
  - **dotnet tool 命令名**：`tslite` → `sndb`。
  - **ADO.NET 连接字符串 scheme**：`tslite://` / `tslite+http://` / `tslite+https://` → `sonnetdb://` / `sonnetdb+http://` / `sonnetdb+https://`。
  - **Docker 镜像**：`iotsharp/tslite-server` → `iotsharp/sonnetdb`，`ghcr.io/<owner>/tslite-server` → `ghcr.io/<owner>/sonnetdb`。
  - **环境变量前缀**：`TSLITE_*` → `SONNETDB_*`。
  - **Prometheus 指标前缀**：`tslite_*` → `sonnetdb_*`。
  - **解决方案 / 目录**：`TSLite.slnx` → `SonnetDB.slnx`；`src/TSLite*` 与 `tests/TSLite*` 整体迁移至 `src/SonnetDB*` 与 `tests/SonnetDB*`。
  - **服务端 Bundle / 安装包**：`tslite-server-full-<ver>-<rid>` → `sonnetdb-full-<ver>-<rid>`；启动脚本 `start-tslite-server.{cmd,sh}` → `start-sonnetdb.{cmd,sh}`；Linux 包路径 `/opt/tslite-server` → `/opt/sonnetdb`。
  - **代码命名空间**：`TSLite.*` → `SonnetDB.*`，`TSLite.Server.*` → `SonnetDB.*`（服务端去掉 `.Server` 子命名空间，与对外品牌一致）。
  - **保留**：核心类型名 `Tsdb` / `TsdbOptions` / `SndbConnection` 等不变（`Tsdb` 是通用时序库缩写而非品牌词）。
- **版本升级**：`0.1.0` → `1.0.0`。

### Planned
- **Milestone 13 — 向量类型与嵌入式向量索引（Copilot 知识库底座）**：`VECTOR(dim)` 字段类型 + `cosine_distance` / `l2_distance` / `inner_product` 标量函数 + `knn(measurement, column, query, k)` 表值函数；首版 brute-force + `System.Numerics.Tensors.TensorPrimitives`（Safe-only），HNSW 作为可选段内 sidecar 索引（`SDBVIDX`）。详见 ROADMAP PR #58 ~ #62。
- **Milestone 14 — SonnetDB Copilot：MCP 工具 + 知识库 + 智能体**：基于 Microsoft Agent Framework 新建独立项目 `src/SonnetDB.Copilot/`，复用现有 `/mcp/{db}` 工具集 + Milestone 13 的向量召回，把"用户文档 / 技能库 / 数据库 schema"统一存入 `__copilot__` 系统库（dogfooding）。Embedding/Chat 走统一 `IEmbeddingProvider` / `IChatProvider` 抽象，**本地 ONNX（bge-small-zh）** 与 **OpenAI 兼容端点（国际 / 国内任意 OpenAI-compat 网关）** 同时支持，可按部署场景切换。新增 HTTP 端点 `POST /v1/copilot/chat`（NDJSON / SSE 流式）+ Web Admin Chat Tab。详见 ROADMAP PR #63 ~ #69。

### Added
- **PR #58 a — `FieldValue.Vector` 与 WAL `WritePoint` Vector 编解码（Milestone 13 第一切片）**
  - 新增 `FieldType.Vector = 5`：定长 32 位浮点向量，dim 由后续 schema 声明，WAL 内按 `dim(4) + dim×float32(LE)` 排布。
  - `FieldValue` 新增 `Vector` 分支：`FromVector(ReadOnlyMemory<float>)` / `FromVector(float[])` 工厂；`AsVector()` / `VectorDimension` 取值；`Equals` 全量序列比较，`GetHashCode` 采样首/中/末三分量；`ToString` 形如 `vector(N)[a,b,...]`，超过 8 维自动截断。
  - `WalPayloadCodec`：`MeasureWritePoint` / `WriteWritePointPayload` / `ReadWritePointPayload` 三处支持 `FieldType.Vector`；新增 `ReadVectorPayload`，对 `valueLen != 4 + dim*4`、`dim < 1` 等坏 payload 抛 `InvalidDataException`。
  - 兼容性：`FileHeader.Version` 暂未升级（本切片只动 WAL `WritePoint` 序列化层；Schema/Segment 层尚未声明 Vector 列，因此现有 segment 文件格式完全不变）。Schema VECTOR(dim) 列、`BlockEncoding.VectorRaw`、SQL 字面量与 `FileHeader` v3 升级将随 PR #58 后续切片合入。
  - 测试：`FieldValueTests` 新增 13 个用例（Vector round-trip / Equals / dim 不等 / `AsDouble` 抛异常 / `TryGetNumeric=false` / ToString 截断）；`WalPayloadCodecTests` 新增 4 个 dim variant（1 / 3 / 8 / 384）的 WritePoint round-trip。全量回归 1520 + 116 = 1636 通过。

- **服务端内建 MCP（Model Context Protocol）只读入口**
  - `src/SonnetDB` 新增基于官方 `ModelContextProtocol.AspNetCore` 1.2.0 的 Streamable HTTP MCP 端点：`/mcp/{db}`。启用 `Stateless=true`，关闭 legacy SSE，`ConfigureSessionOptions` 会把当前数据库名写入 `ServerInstructions`，明确这是绑定到单个数据库的只读 SonnetDB MCP 会话。
  - 新增 MCP 上下文解析与预校验：所有 `/mcp/{db}` 请求在进入 MCP SDK 前先复用现有 `TsdbRegistry` 校验数据库名与存在性；非法库名返回 `400 bad_request`，不存在数据库返回 `404 db_not_found`，并把当前 `db` 与 `Tsdb` 实例缓存到 `HttpContext.Items` 供 tools/resources 读取。
  - 新增只读 MCP tools：
    - `query_sql(sql, maxRows)`：仅允许 `SELECT` / `SHOW MEASUREMENTS` / `SHOW TABLES` / `DESCRIBE [MEASUREMENT]`；对 `SELECT` 在 AST 层自动补/收紧分页，并采用“多抓 1 行”检测 `truncated`。
    - `list_measurements(maxRows)`：返回当前数据库 measurement 名列表。
    - `describe_measurement(name)`：返回指定 measurement 的 tag/field schema。
  - 新增 MCP resources：
    - `sonnetdb://schema/measurements`
    - `sonnetdb://schema/measurement/{name}`
    - `sonnetdb://stats/database`
    三个资源统一返回 `application/json` 文本，分别暴露 measurement 列表、单 measurement schema 与当前数据库统计（measurement 数、segment 数、memtable 点数、checkpoint LSN 等）。
  - 新增 `src/SonnetDB/Mcp/` 实现层：结果 DTO、`JsonElementValue` 转换、只读 SQL 裁剪逻辑、`CallToolResult`/`TextResourceContents` 构造与基于 `IHttpContextAccessor` 的数据库绑定上下文解析。
  - 测试：新增 `tests/SonnetDB.Tests/McpEndToEndTests.cs`，通过真实 Kestrel + `McpClient` 覆盖 `list_tools`、`query_sql` 自动截断、`list_measurements`、`describe_measurement`、`list_resources` / `list_resource_templates` / `read_resource` 的端到端路径。
  - 权限模型补齐：`/mcp/{db}` 与其余数据库作用域 HTTP 入口现在会把“动态用户 token”映射到 `GrantsStore` 的数据库级 `Read/Write/Admin` 权限；静态 `ServerOptions.Tokens` 仍保持全局 role 语义。无 grant 的用户访问数据库作用域 MCP / SQL / schema / bulk 端点将返回 `403 forbidden`，superuser 保持全放行。
  - 数据库可见性补齐：`GET /v1/db` 与数据面 SQL 中的 `SHOW DATABASES` 现在会按当前请求可见范围过滤数据库列表。普通动态用户只会看到自己有 `Read/Write/Admin`（含 `*` 通配）权限的数据库；静态全局 token 与 superuser 继续看到全部数据库。
  - AI 权限补齐：`POST /v1/ai/chat` 在 `mode=sql_gen` 且携带 `db` 时，现在会先校验数据库名、数据库存在性与当前请求对该数据库的 `Read` 权限，再拼接 schema 系统提示词；未授权用户无法再借助 AI SQL 生成读取其他数据库的 measurement/column 元数据。
  - SSE 权限补齐：`GET /v1/events` 对动态用户 token 的数据库相关事件现在按 grant 实时过滤。`db` 与 `slow_query` 只会下发当前用户对该数据库具备 `Read` 以上权限的事件，控制面慢查询（`__control`）对普通动态用户隐藏；`metrics` 事件中的 `databases` 与 `perDatabaseSegments` 也会裁剪为当前用户可见数据库集合，避免从实时事件流泄露未授权数据库名。
  - 控制面自服务权限补齐：普通动态用户现在可以通过 `/v1/sql` 或有权访问的 `/v1/db/{db}/sql` 执行 `SHOW GRANTS` / `SHOW TOKENS` / `ISSUE TOKEN FOR <self>` / `REVOKE TOKEN '<self-token-id>'` 等“只操作自己”的控制面语句；对其他用户的授权或 token 执行查询、签发、吊销将返回 `403 forbidden`。`SHOW USERS`、用户管理、授权管理、数据库管理等仍保持 admin-only。

- **元数据 SQL：`SHOW MEASUREMENTS` / `SHOW TABLES` / `DESCRIBE [MEASUREMENT] <name>`**
  - 新增 AST 节点 `ShowMeasurementsStatement` 与 `DescribeMeasurementStatement`；`SqlLexer` 增加关键字 `MEASUREMENTS` / `TABLES` / `DESCRIBE` / `DESC`；`SqlParser` 在 `SHOW` 分支识别 `MEASUREMENTS` 和兼容别名 `TABLES`，并新增顶层 `DESCRIBE` / `DESC` 入口（关键字 `MEASUREMENT` 可省略）。
  - `SqlExecutor` 新增 `ShowMeasurements(Tsdb)` 与 `DescribeMeasurement(Tsdb, name)` 执行路径，统一返回 `SelectExecutionResult`：`SHOW MEASUREMENTS` / `SHOW TABLES` 输出单列 `name`（按字典序升序）；`DESCRIBE` 输出三列 `column_name` / `column_type`（`tag` / `field`）/ `data_type`（`float64` / `int64` / `boolean` / `string`），按 schema 声明顺序返回。
  - 引入 `SHOW TABLES` / `DESC` 兼容别名以适配 DBeaver / DataGrip / 通用 ADO.NET schema 浏览器。
  - 测试：新增 `tests/SonnetDB.Core.Tests/Sql/SqlExecutorMetadataTests.cs`，11 个用例覆盖空库 / 字典序排序 / `SHOW TABLES` 等价 / `DESCRIBE`+`DESC` 等价 / 关键字 `MEASUREMENT` 可省略 / 不存在 measurement 抛 `InvalidOperationException` / Parser AST 形状校验。

- **数据面分页：`OFFSET/FETCH` + `LIMIT` 兼容语法**
  - `SELECT` 新增可选分页子句：支持 SQL 标准风格 `OFFSET <n> [ROW|ROWS] FETCH FIRST|NEXT <m> ROW|ROWS ONLY`，以及兼容风格 `LIMIT <m> [OFFSET <n>]`。
  - AST `SelectStatement` 增加 `Pagination` 参数，执行层在最终结果集统一应用分页切片，覆盖 raw / aggregate / TVF 三条路径。
  - 为避免与聚合函数 `first(...)` 冲突，`FIRST/NEXT/ROW/ROWS/ONLY` 按普通标识符词法处理，仅在 `FETCH` 子句按上下文识别。
  - 测试：补充 parser / lexer / executor 用例，覆盖 `LIMIT`、`OFFSET`、`FETCH` 语义以及越界 offset 返回空集。

- **Milestone 12 — PR #57：函数族基准 + README 函数支持矩阵**
  - 新增 `tests/SonnetDB.Benchmarks/Benchmarks/FunctionBenchmark.cs`：以 50,000 个数据点为样本，对 PR #50 ~ #56 引入的窗口 / 聚合 / TVF 函数族走完整 SqlParser → SqlExecutor 流水线的端到端基准。覆盖 SonnetDB 自身的 `derivative` / `moving_average` / `ewma` / `holt_winters` / `anomaly(zscore)` / `p99` / `distinct_count` / `forecast(linear)` / `forecast(holt_winters)` 9 项基线，以及 InfluxDB Flux（`derivative` / `movingAverage` / `holtWinters` / `quantile(method:"estimate_tdigest")`）与 TDengine REST（`DERIVATIVE` / `MAVG` / `PERCENTILE`）的等价语义对照；外部数据库不可用时按 `[SKIP]` 提示，不阻塞 SonnetDB 基线运行。
  - `README.md` 新增「支持的 SQL 函数」矩阵章节：按 PR 引入顺序枚举 PR #50 ~ #56 全部内置函数（聚合 / 标量 / 窗口 / TVF）共 50+ 项，并列出 InfluxDB / Timescale / TDengine / Prometheus 的对标函数与备注；同步在 `README.en.md` 增加「Built-in SQL functions」英文版矩阵。
  - 矩阵章节同时指向 `docs/extending-functions.md`（UDF 注册）与 `tests/SonnetDB.Benchmarks/Benchmarks/FunctionBenchmark.cs`（性能对照），使函数体系的「能做什么 / 怎么扩展 / 性能如何」三条线索从 README 一处可达。

- **Milestone 12 — PR #56：Tier 5 用户自定义函数（UDF）注册 API**
  - 新增公开类型 `SonnetDB.Query.Functions.UserFunctionRegistry`：按 `Tsdb` 实例隔离的 UDF 注册表，挂在新增的 `Tsdb.Functions` 属性上。提供 `RegisterScalar(name, evaluator, min, max)` / `RegisterScalar(IScalarFunction)` / `RegisterAggregate(IAggregateFunction)` / `RegisterWindow(IWindowFunction)` / `RegisterTableValuedFunction(name, executor)` 五条注册路径，以及 `Unregister(name)` 与 `TryGet*` 查询。聚合 UDF 强制 `LegacyAggregator == null`（仅内置 7 个聚合可用 legacy fast-path）；TVF UDF 不允许使用保留名 `forecast`。
  - 通过 `AsyncLocal<UserFunctionRegistry?>` + `UserFunctionRegistry.AmbientScope` 提供查询作用域 ambient；`SqlExecutor.ExecuteSelect` 在执行前 `EnterScope(tsdb.Functions)`、退出时自动恢复，确保多 `Tsdb` 实例并发执行 SQL 时互不可见。
  - `FunctionRegistry.GetFunctionKind` / `TryGetAggregate` / `TryGetScalar` / `TryGetWindow` 全部改为「优先查 ambient UDF，未命中再回退内置」，保持 PR #50~#55 的所有内置函数行为零变化。`TableValuedFunctionExecutor.Execute` 同样优先匹配用户 TVF，再走内置 `forecast` 路由。
  - 新增 `TsdbOptions.AllowUserFunctions` 选项（默认 `true`，嵌入式启用）；`SonnetDB.Hosting.TsdbRegistry` 在两条 `Tsdb.Open` 调用上将其设为 `false`，从而 Server / HTTP 模式默认禁用 UDF 以保证 AOT 兼容（`Functions.IsEnabled == false`，所有 `Register*` 抛 `InvalidOperationException`）。
  - 新增 `docs/extending-functions.md`：覆盖标量 / 聚合 / 窗口 / TVF 四类 UDF 的注册示例（`Func` 委托形态 + `IScalarFunction` 等接口形态）、`Merge` 可结合性约束、跨实例隔离、`forecast` 保留名、Server 模式禁用策略与 UDF 不覆盖的功能边界。
  - 新增测试：`tests/SonnetDB.Core.Tests/Query/Functions/UserFunctionRegistryTests.cs`（9 项端到端）：委托标量 UDF 解析、UDF 覆盖同名内置（`abs` 路径）、聚合 UDF 通过 `IAggregateAccumulator` 接入 SELECT、TVF UDF 路由 + 行集构造、TVF 保留名 `forecast` 拒绝、`Unregister` 后查询失败、`AllowUserFunctions=false` 时 Register 全部抛、ambient 在两个 `Tsdb` 实例间隔离、聚合 UDF 设置 `LegacyAggregator` 时拒绝注册。

- **Milestone 12 — PR #55：Tier 4 Forecast TVF + 异常 / 变点检测**
  - 新增公开类型 `SonnetDB.Query.Functions.Forecasting.TimeSeriesForecaster`：纯 C#、零外部依赖的预测库 API，提供 `Forecast(long[] timestampsMs, double[] values, int horizon, ForecastAlgorithm algorithm, int season = 0)`，输出 `ForecastPoint[] (TimestampMs, Value, Lower, Upper)`；支持 **线性最小二乘外推** 与 **Holt / Holt-Winters 三次指数平滑（加性季节）**，置信区间按残差 RMSE × $z_{0.975}$ × $\sqrt{h+1}$ 给出。
  - 新增 SQL 表值函数 `forecast(measurement, field, horizon, 'algo'[, season])`：在 `FROM` 子句中作为数据源，按 measurement / FIELD 拉取历史数据并按 series 维度独立预测；输出列 `(time, value, lower, upper, ...tag_columns)`。Parser 在 `ParseSelect()` 中识别 `FROM <ident>(` 调用形态并填充 `SelectStatement.TableValuedFunction`；新增 `TableValuedFunctionExecutor` 路由器：校验参数（measurement / FIELD 标识符、`horizon` 正整数字面量、`'linear'` / `'holt_winters'` / `'hw'` 算法、可选 `season` 非负整数），复用 `WhereClauseDecomposer` 处理标签过滤，按 series 调用 `TimeSeriesForecaster.Forecast` 并按预测点落行。当前要求 `SELECT *`。
  - 新增 SQL 窗口函数 `anomaly(field, 'zscore' | 'mad' | 'iqr', threshold)`：行流→行流，输出 `bool?`；`zscore` 用样本标准差（N−1），`mad` 用 1.4826 × MAD 鲁棒尺度，`iqr` 用 0.25 / 0.75 分位线性插值 + Tukey $k$ 倍 IQR 围栏。`null` 输入透传 `null`。
  - 新增 SQL 窗口函数 `changepoint(field, 'cusum', threshold[, drift])`：双边 CUSUM 累积和变点检测；用前 `max(5, n/4)` 个非空样本估计基线均值与样本标准差以避免变点本身污染参考；触发后累积器复位以探测下一个变点。`drift` 默认 `0.5`。
  - `FunctionRegistry` 注册 `anomaly` 与 `changepoint` 为 Tier 3 窗口函数，与 PR #53 框架共享 `IWindowFunction` / `IWindowEvaluator` 协议。
  - 新增 `docs/forecast.md`：完整覆盖 SQL 语法、输出列、算法公式、嵌入式库 API、局限与与 UDF 扩展的关系。
  - 新增测试：`tests/SonnetDB.Core.Tests/Query/Functions/TimeSeriesForecasterTests.cs`（7 项算法层单测：线性外推方向 / 截距、Holt-Winters 季节恢复、置信区间宽度、退化输入、空 / 单点输入边界）+ `AnomalyChangepointFunctionTests.cs`（8 项窗口函数单测：z-score / MAD / IQR 各方法、`null` 输入、常数序列、CUSUM 检测均值漂移并触发后复位）+ `tests/SonnetDB.Core.Tests/Sql/SqlExecutorForecastTests.cs`（8 项 SQL 端到端：线性 / HW 预测列形态、与 WHERE 标签过滤组合、参数校验、`SELECT *` 强制、anomaly / changepoint 输出 bool 列）。

- **Milestone 12 — PR #54：Tier 4 PID 控制律内置函数 + 自动整定库 API**
  - 新增公开类型 `SonnetDB.Query.Functions.Control.PidController`：纯 C# 离散 PID 状态机，提供 `Update(timestampMs, processVariable, setpoint)`、`Snapshot()` / `Restore(...)` 与 `Reset()`；首行只输出比例项（无 dt 参考），$\Delta t \le 0$ 时跳过 I/D 更新避免发散；状态结构 `PidControllerSnapshot { Integral, PrevError, PrevTimeMs, HasHistory }`。
  - 新增 SQL 行级窗口函数 `pid_series(field, setpoint, kp, ki, kd)`（`IWindowFunction`）：与 PR #53 窗口算子框架共享 `Compute(long[] timestamps, FieldValue?[] values)` 协议，按 series 独立维护控制器实例；`null` 输入透传 `null` 输出且不推进状态。
  - 新增 SQL 聚合函数 `pid(field, setpoint, kp, ki, kd)`（`IAggregateFunction` + `PidAccumulator`）：与 `GROUP BY time(...)` 组合时桶内逐行推进、桶尾输出最终 $u(t)$；`Merge` 取 `PrevTimeMs` 更晚的段以支持跨段拼接；拒绝 `pid(*)`、错误参数个数、非 FIELD 列、字符串字段。
  - `IAggregateAccumulator` 增加默认接口方法 `Add(long timestampMs, double value) => Add(value);`；`SelectExecutor.AggSlot.Update` 改为通过该重载传递时间戳，对既有 Welford / TDigest / HLL / Histogram 累加器**零行为变化**。
  - `FunctionRegistry` 注册 `pid` 为 `Aggregate`、`pid_series` 为 `Window`，纳入既有 `GetFunctionKind` / `TryGetAggregate` / `TryGetWindow` 路由。
  - 既有 `PidParameterEstimator`（Sundaresan & Krishnaswamy 35%/85% 两点法识别 FOPDT 模型 + Ziegler-Nichols / Cohen-Coon / Skogestad IMC 三种整定规则）保持库级 API；同时新增 SQL 聚合函数 `pid_estimate(field, method, step_magnitude, initial_fraction, final_fraction, imc_lambda)`，对结果集中 (time, value) 样本调用辨识 + 整定，输出 JSON `{"kp":..,"ki":..,"kd":..}`。`method` 接受字符串字面量 `'zn'` / `'cc'` / `'imc'` 或 NULL（默认 ZN），数值参数允许 NULL 取默认值。`docs/pid-control.md` 提供端到端工作流（采集 → 离线整定 → SQL 回测 → 控制回写 → 监控）与 SQL/库 API 双形态示例。
  - 新增测试：`tests/SonnetDB.Core.Tests/Query/Functions/Control/PidControllerTests.cs`（14 项控制器与累加器/求值器单元测试）+ `tests/SonnetDB.Core.Tests/Sql/SqlExecutorPidFunctionTests.cs`（10 项 SQL 端到端：行级输出、桶级最终 u、与时间/字段投影混合、负增益字面量、参数校验、Tag/字符串列拒绝、控制回写两步流程）。

- **Milestone 12 — PR #53：Tier 3 窗口算子框架（17 个窗口函数）**
  - 新增公共契约 `IWindowFunction` / `IWindowEvaluator`：window 函数为「行流→行流」的逐序列算子，由 `CreateEvaluator(call, schema)` 工厂在查询计划阶段完成参数解析与字段绑定，运行阶段调用 `Compute(long[] timestamps, FieldValue?[] values) → object?[]` 输出与输入等长的列结果。
  - 落地 17 个窗口函数（位于 `src/SonnetDB/Query/Functions/Window/`），按语义分为 5 组：
    - **差分类**：`difference` / `delta`（当前 − 上一行）；`increase`（仅保留正差，counter 重置返回 `null`）；`derivative` / `non_negative_derivative` / `rate` / `irate`（按时间归一化的瞬时变化率，可指定单位 `1s` / `100ms` 等）。
    - **累计类**：`cumulative_sum`（运行总和，首个有效值前为 `null`）；`integral(field [, unit])`（梯形面积，默认按秒积分）。
    - **平滑类**：`moving_average(field, n)`（N 点滑动平均，前 N−1 行返回 `null`）；`ewma(field, alpha)`（指数加权移动平均，校验 `alpha ∈ (0, 1]`）；`holt_winters(field, alpha, beta)`（加性 Holt 双指数平滑，无季节性，校验 `alpha`、`beta` ∈ `(0, 1]`）。
    - **缺失值处理**：`fill(field, value)`（数值字面量填充 `null`，支持 `-1` 等带负号字面量）；`locf(field)`（last observation carried forward）；`interpolate(field)`（两遍扫描线性插值，前导/尾随 `null` 保持 `null`）。
    - **状态分析**：`state_changes(field)`（基于 `FieldValue.Equals` 的状态切换计数，支持 string/bool）；`state_duration(field)`（当前状态持续毫秒数，状态切换时归零）。
  - `FunctionRegistry` 新增 `WindowFunctions` 集合 + `TryGetWindow(name, out function)` API；`GetFunctionKind` 新增 `FunctionKind.Window` 分支。
  - `SelectExecutor` 新增 `ProjectionKind.Window` 投影类别：`ClassifyProjections` 将 `Window` 函数路由到该类别；`ExecuteRaw` 在每个 series 内构造 `long[] timestamps` + 与之对齐的 `FieldValue?[]`，预计算所有 window evaluator 输出后按行下标注入结果，与 `time` / `tag` / `field` / `scalar` 投影任意组合。窗口函数与聚合函数互斥（沿用既有 `_ → 内部错误` 拒绝路径）。
  - 新增 `tests/SonnetDB.Core.Tests/Query/Functions/WindowFunctionTests.cs`（35 项，覆盖各 evaluator 的纯算法语义、`Welford` / counter 重置 / 梯形积分 / 线性插值 / EWMA 递推 / 状态分析等基线，外加全部 17 个函数的 `FunctionRegistry` 注册校验）与 `tests/SonnetDB.Core.Tests/Sql/SqlExecutorWindowFunctionTests.cs`（21 项 SQL 端到端，覆盖各窗口函数典型用法、与 `time` / `field` / `tag` 投影混合、与聚合互斥、参数错误、字符串/Tag 字段拒绝等）。

- **Milestone 12 — PR #52：Tier 2 扩展聚合（可合并累加器）**
  - 新增 9 个扩展聚合函数：`stddev` / `variance` / `spread` / `mode` / `median` / `percentile(field, q)` / `p50` / `p90` / `p95` / `p99` / `tdigest_agg` / `distinct_count` / `histogram(field, bin_width)`，全部走新增的 `IAggregateAccumulator` 路径，与既有 7 个 legacy 聚合并存、零性能回归。
  - 新增公共契约 `IAggregateAccumulator`（`Count` / `Add` / `Merge` / `Finalize`）与 `IAggregateFunction.CreateAccumulator(call, schema)`（默认实现返回 `null`），用于让扩展聚合声明跨段、跨桶、跨序列可合并的中间状态。
  - 新增三类核心累加器算法（位于 `src/SonnetDB/Query/Functions/Aggregates/`）：
    - **Welford** 在线方差/标准差，附 Chan-Golub-LeVeque 并行合并公式；样本数 < 2 时 `stddev` 返回 `null`。
    - **TDigest** 简化的 Ben Haim merging digest（compression=200、k(q) ≈ 4q(1−q)/δ），支持 `Add` / `Merge` / `Quantile` / `Serialize` / `ToJson`，作为 `percentile` / `pXX` / `median` / `tdigest_agg` 的统一后端。
    - **HyperLogLog**（precision 14、16384 寄存器、AlphaMM 修正、小基数 linear-counting），作为 `distinct_count` 的统一后端，使用 `System.IO.Hashing.XxHash64` 哈希双精度浮点的 IEEE 字节序。
  - `SelectExecutor.ExecuteAggregate` 重构为 `AggSlot` 分发：legacy 聚合走原 `BucketState`，扩展聚合走 `IAggregateAccumulator`；`AggSpec` 新增 `IsExtended` / `IsCountStar` 与字段解析支持，`first` / `last` 多序列保护仅作用于 legacy 路径。
  - `histogram(field, bin_width)` 输出 `{"[lo,hi)":n,...}` 格式 JSON，跨段合并时校验 `bin_width` 一致性；`tdigest_agg` 输出可后续合并的 JSON 状态串。
  - 新增 `tests/SonnetDB.Core.Tests/Query/Functions/ExtendedAggregateAccumulatorTests.cs`（14 项单元测试，覆盖 Welford / TDigest / HLL 合并一致性、空集 / 单点边界、参数校验）与 `tests/SonnetDB.Core.Tests/Sql/SqlExecutorExtendedAggregateTests.cs`（13 项 SQL 端到端测试，覆盖单序列、多序列合并、`GROUP BY time(...)` 桶聚合、混合 legacy + 扩展聚合、参数错误）。

- **PR #39：Docker 镜像自动发布**
  - 新增 `.github/workflows/docker-publish.yml`：在 `main` 分支相关文件变更、`v*` 标签或手动触发时，自动构建 `src/SonnetDB/Dockerfile` 并推送镜像。
  - 目标镜像仓库：
    - Docker Hub：`iotsharp/sonnetdb`
    - GHCR：`ghcr.io/<owner>/sonnetdb`
  - 标签策略覆盖 `latest`、`edge`、`vX.Y.Z`、`X.Y` 与 `sha-<commit>`，并写入 OCI labels。
  - 工作流接入 `docker/setup-buildx-action`、`docker/metadata-action`、`docker/login-action`、`docker/build-push-action`，并启用 GitHub Actions Docker layer cache。
- 新增 `docs/releases/docker-image.md`，补齐 Docker 镜像的启动方式、标签策略、自动发布触发条件和仓库 Secrets 要求。

- **PR #37b：GitHub Pages 文档自动发布**
  - 新增 `.github/workflows/docs-pages.yml`：在 `main` 分支文档变更或手动触发时，自动执行 `dotnet tool restore` + `jekyllnet build`，并通过 GitHub Pages 官方 Actions 上传和部署静态文档站点。
  - Pages 构建阶段会基于仓库名动态注入文档基址（例如 `/SonnetDB`），因此无需维护第二套独立文档源码。

- **Milestone 12 — PR #51：Tier 1 标量函数 + SQL 函数调用扩展**
  - 新增公共类型 `ISqlFunction` / `IScalarFunction` / `FunctionKind`，并扩展 `FunctionRegistry` 的 `TryGetScalar(name, out function)` / `ScalarFunctions` / `GetFunctionKind(name)` API；`IAggregateFunction` 改为继承 `ISqlFunction` 共享 `Name` 契约。
  - 落地内置标量函数：`abs` / `round(value[, digits])` / `sqrt` / `log(value[, base])` / `coalesce(...)`；统一在 `BuiltInScalarFunction` 中做参数个数校验，并通过 `RequireDouble` 兼容 byte/int/long/float/double/decimal。
  - `SelectExecutor` 新增 `ProjectionKind.Scalar` 投影路径，支持在 SELECT 投影中嵌套调用、与算术表达式混用，并自动汇总标量函数引用的字段名加入 `QueryFieldValues` 的字段集；为 `Window` / `TableValued` 类别预留诊断分支，便于 PR #53 / #55 接入。
  - **AST 重构（破坏性内部 API）**：`SelectStatement.GroupByTime: TimeBucketSpec?` 替换为 `SelectStatement.GroupBy: IReadOnlyList<SqlExpression>`，由 `SelectExecutor.ResolveGroupByTime` 在执行阶段把 `time(duration)` 形式归约为 `TimeBucketSpec`；`SqlParser.ParseGroupByList` / `ParseGroupByExpression` 取代原 `ParseGroupByTime`，仍在 parser 阶段拒绝 `time(0)` 之类的非法 duration。无 GROUP BY 时 `GroupBy` 为空集合（不再为 `null`）。
  - 新增/扩展测试：`tests/SonnetDB.Core.Tests/Sql/SqlExecutorSelectTests.cs` 覆盖标量函数投影、`coalesce` 跨字段时间轴、别名、未知函数与参数个数错误；`SqlParserTests` 覆盖 `GROUP BY time(...)` 解析为 `FunctionCallExpression` 与标量函数调用解析；`FunctionRegistryTests` 增加 `TryGetScalar` / `GetFunctionKind` 与标量函数求值校验。
  - `docs/sql-reference.md` 补充 SELECT 投影中标量函数的支持范围、嵌套规则与 `coalesce` 时间轴说明。

- **Milestone 12 — 函数注册表基础设施（`FunctionRegistry`）**：新增 `src/SonnetDB/Query/Functions/` 目录，承载 Tier 1~3 函数扩展（PR #51~#57）所需的注册与解析骨架，零第三方依赖。
  - 新增公共类型 `FunctionRegistry`（静态注册表）+ `IAggregateFunction`（命名 / SQL 调用语法校验 / `LegacyAggregator` 桥接），通过 `TryGetAggregate(name, out function)` 与 `GetAggregate(Aggregator)` 双向查找内置 7 个聚合（count/sum/min/max/avg/first/last）。
  - `BuiltInAggregateFunction.ResolveFieldName` 集中实现 `*` 形式校验（仅 `count(*)` 允许）、参数个数校验、列存在性、Tag/Field 角色与 String 类型限制。
  - 重构 `SelectExecutor`：移除内部硬编码 `_aggregateFunctions` HashSet 与 `switch` 解析逻辑，改为统一走 `FunctionRegistry`；保留现有高性能 `Aggregator` 枚举执行路径，本 PR 不影响查询性能。
  - 新增 `tests/SonnetDB.Core.Tests/Query/Functions/FunctionRegistryTests.cs`：8 项单元测试覆盖大小写不敏感、未知函数、`count(*)` 接受 / 其他函数拒绝 `*`、Tag 列拒绝、String 字段拒绝、合法字段返回原列名等关键分支。
  - 新增 `tests/SonnetDB.Core.Tests/Sql/SqlExecutorSelectTests.cs::Select_SumStar_Throws` / `Select_AggregateLookup_IsCaseInsensitive`，验证 SelectExecutor 经注册表后的对外行为不变。

- **Milestone 12 — PID 参数估算（`PidParameterEstimator`）**：新增 `src/SonnetDB/Query/Functions/Control/` 目录，提供从历史阶跃响应时序数据自动推算 PID 控制器参数的纯 C# 实现，零第三方依赖。
  - `PidTuningMethod` 枚举：`ZieglerNichols`（Ziegler-Nichols 阶跃响应法）/ `CohenCoon`（Cohen-Coon 法）/ `Imc`（Skogestad SIMC/IMC 法）。
  - `PidParameters` 记录类型：封装 `Kp`（比例增益）、`Ki`（积分增益）、`Kd`（微分增益）。
  - `PidEstimationOptions`：可选配置项，包含整定方法、阶跃幅度 `StepMagnitude`、IMC 闭环时间常数 `ImcLambda`、初始/末尾稳态窗口比例。
  - `PidParameterEstimator.Estimate`：接受 `IReadOnlyList<(long TimestampMs, double Value)>` 或 `IReadOnlyList<DataPoint>`，采用 **Sundaresan & Krishnaswamy 35%/85% 两点法** 辨识一阶纯滞后（FOPDT）模型（K、τ、θ），再按所选整定规则输出 Kp/Ki/Kd。
  - 新增 `tests/SonnetDB.Core.Tests/Query/Functions/Control/PidParameterEstimatorTests.cs`：20 项单元测试，覆盖三种整定方法的数值精度验证、非零基线、DataPoint 重载、Int64 字段值、边界/错误校验、负向阶跃及三种方法正参数一致性。

### Changed
- `README.md` 与发布文档新增预编译 Docker 镜像入口，支持直接通过 `docker run iotsharp/sonnetdb:latest` 启动服务端。
- `ROADMAP.md` 中 `PR #39` 状态更新为已完成，Milestone 9 随之闭环。

- 文档站点模板与交叉链接支持双部署模式：
  - `docs/_config.yml` 新增 `docs_baseurl`、`app_link_url`、`app_link_text`、`home_primary_url`、`home_primary_text` 配置项。
  - `docs/_layouts/default.html` 与多篇文档内的站内链接改为基于配置拼接，默认继续服务于 `SonnetDB` 的 `/help/`，同时兼容 GitHub Pages 的仓库子路径。
- 将 `ROADMAP.md` 中的 `PR #37b` 标记为已完成，并更新 Milestone 9 的当前推进顺序。

- 核查并修正 `ROADMAP.md` 中的 Milestone 9 状态：
  - 补记已在仓库落地但路线图遗漏的 `PR #36`（`SonnetDB` Docker 基准环境与 `ServerBenchmark`）。
  - 将 `PR #38` 调整为已完成：仓库已具备 `eng/release.ps1`、`.github/workflows/publish.yml`、NuGet 包元数据、SDK / Server Bundle 与 `msi` / `deb` / `rpm` 打包能力。
  - 将原 `PR #37` 拆分为 `#37a`（已完成：文档重写、JekyllNet `/help` 站点、README 与发布文档核对）和 `#37b`（现已完成：GitHub Pages 自动发布流水线），使路线图状态与当前代码一致。

- **PR #50：查询元数据快路径 — Format v2 + 跨桶融合 + MemTable 增量聚合 + ExecuteMany 共享快照**（**Breaking 段文件格式变更**）
  - **Format v2**（破坏性，不兼容旧 segment 文件）：`BlockHeader` 由 64 B 扩展到 72 B，将 `AggregateMinBits` / `AggregateMaxBits` 由 `int`（4 B）升级为 `AggregateMin` / `AggregateMax`（`double`，8 B），覆盖 Float64 任意值与 ±2^53 内的 Int64。新增 `TsdbMagic.SegmentFormatVersion = 2` 常量与 `TsdbMagic.FormatVersion = 1`（用于 FileHeader/WAL/Catalog）解耦；`SegmentHeader.IsValid` 与 `SegmentReader` 拒绝读取 v1 段文件并给出明确升级提示。`SegmentWriter.TryBuildAggregateMetadata` 不再因窄类型截断而放弃 `HasMinMax`，对所有数值字段一律写入 `HasSumCount | HasMinMax`。`SegmentReader` 删除 `DecodeAggregateBoundary` 私有桥接，直接读取 8 B `double`。**升级路径：删除 `*.SDBSEG` 后启动可由 WAL 重放重新生成 v2 段；混用旧版本数据将抛 `SegmentCorruptedException`。**
  - **跨桶 block 元数据下推**：`QueryEngine` 新增 `CanFuseDeltaTimestampInline` + `FuseDeltaBlockToGlobal` / `FuseDeltaBlockToBuckets` 融合内联路径——对 `(delta-of-delta 时间戳 + 原始数值)` 的数值 block，仅租用一份 `ArrayPool<long>` 解码时间戳后内联走 `ReadRawNumericValue` + `AddValueToBucket`，避免 `BlockDecoder.DecodeRange` 物化 `DataPoint[]`。大 block 跨桶聚合分配显著下降。
  - **MemTable 运行期 sum/min/max**：`MemTableSeries` 在 `Append` 阶段对 Float64/Int64/Boolean 增量维护 `_numericSum / _numericMin / _numericMax`，新增 `HasNumericAggregates` / `NumericSum` / `NumericMin` / `NumericMax` / `TryGetNumericAggregateSnapshot` 公共 API；`QueryEngine.ExecuteAggregateFast` 在范围全覆盖且单桶或全局聚合时直接合并 MemTable 元数据，跳过对 `ReadOnlyMemory<DataPoint>` 切片的逐点扫描。`AggregateState` 新增 `AddMemTableAggregate` 合并入口。
  - **ExecuteMany 共享快照**：`QueryEngine.ExecuteMany` 不再每个 series 重建 `BuildReaderMap` 与重新读取 `_segments.Index`，改为外层一次构建并通过新的私有重载 `ExecuteAggregateFast(query, index, readers)` 透传；`ShouldUsePointAggregatePath` 仍按 series 分流到 `Execute` 慢路径以保证 First/Last 与墓碑场景行为不变。
  - **测试**：新增 `MemTableSeriesAggregateTests` 5 项（数值字段增量聚合、String 字段不维护、空序列）+ `QueryEngineV2OptimizationTests` 7 项（Int64 极值/高精度 Float64 round-trip、跨桶大 block 聚合一致性、MemTable 全/部分覆盖路径、`ExecuteMany` 与单次 `Execute` 一致性）；同步更新 `BlockHeaderTests` / `FormatSizesTests` / `SegmentHeaderTests` / `SegmentFooterTests` 以反映 72 B BlockHeader 与 `SegmentFormatVersion = 2`。`SonnetDB.Core.Tests` 1263/1263 + `SonnetDB.Tests` 97/97 通过。

### Changed
- 完善 Block 聚合元数据语义并修复 Min/Max 精度漏洞，新增桶聚合元数据快路径：
  - `BlockHeader.AggregateFlags` 由「0/1 单值」语义改为按位组合：`HasSumCount = 0x01`（sum/count 可信）、`HasMinMax = 0x02`（min/max 无损）。旧 v1 segment 写入的 `1` 自动等价于「仅 HasSumCount」，min/max 不会被误用，无需 Format 版本号变更（仍为 1）。
  - `SegmentWriter` 在 Float64 时只在 `(float)min == min && (float)max == max` 时才置 `HasMinMax`，避免向 `Min`/`Max` 查询返回 float 截断后的错误值；Int64 在 min/max 落入 int32 范围时置 `HasMinMax`；sum/count 仍始终写入。
  - `SegmentReader` 解析两个独立标志，`BlockDescriptor` 新增 `HasAggregateSumCount` / `HasAggregateMinMax`（旧 `HasAggregateMetadata` 改为派生属性）。
  - `QueryEngine.CanUseAggregateMetadata` 改为按聚合函数挑选元数据：`Count` 始终命中（用 `descriptor.Count`），`Sum/Avg` 需 `HasAggregateSumCount`，`Min/Max` 需 `HasAggregateMinMax`。
  - 桶聚合（`AddSegmentBlocksToBuckets`）新增元数据快路径：当 block 完整落入查询范围且 `[MinTimestamp, MaxTimestamp]` 整体落在同一个桶内时，直接合并元数据到桶状态，避免读取 payload 与逐点扫描，写入密集场景对 `SUM/COUNT/AVG/MIN/MAX` 的桶聚合直接获益。
  - 测试：新增 4 项 `QueryEngineAggregateTests`（Float64 非可表示数的 Min/Max 精度、Sum/Avg/Count 仍走快路径、桶 SUM/MIN/MAX 命中元数据、跨桶 block 回退扫描），1 项 `BlockHeaderTests` 校验旧 `flags == 1` 的兼容映射；`SonnetDB.Core.Tests` 1250/1250 + `SonnetDB.Tests` 97/97 通过。
  - 不修改文件二进制格式与 `FileHeader.Version`。

### Added
- 新增跨平台 C# 基准运行入口：`eng/benchmarks/start-benchmark-env/start-benchmark-env.csproj` 负责 Docker Compose 构建、启动、健康等待与停止，`eng/benchmarks/run-benchmarks/run-benchmarks.csproj` 负责调用环境入口并运行 BenchmarkDotNet；根 README 改为嵌入 `docs/assets/benchmark-summary.svg` 基准摘要图，后续刷新性能数字时无需反复改根 README 表格。
- **PR #49：基准刷新 + 对外对比（写入快路径专题收尾）**
  - 「写入：100 万点（单序列）」表新增 PR #47 三条服务端 LP/JSON/Bulk 端点的实测数据（1.10–1.20 s / 34–71 MB），把服务端 vs 嵌入式的写入差距从 ~33.8×（SQL Batch 路径）收敛展示到 **~1.77–1.93×**（绕开 SQL parser 路径）。
  - 「嵌入式 vs. SonnetDB 同机对比」表把写入行拆分为 SQL Batch + LP + JSON + Bulk 四行，标注各自的额外开销与主要来源。
  - 「批量入库快路径」段补充 PR #48 `?flush=` 三档位语义表（None / Async / Sync 与适用场景）。
  - 「小结」最后一行更新为反映 PR #47/#48 后的写入收敛事实。
  - **新增 `InsertBenchmark.TDengine_InsertSchemaless_1M`**：走 TDengine InfluxDB-compat 端点 `POST /influxdb/v1/write?precision=ms`，按 100,000 行/批切片避开 taosadapter 16 MB body 上限；`TDengineRestClient` 配套新增 `WriteLineProtocolAsync(db, lp, precision)`。配合 `bench_insert_schemaless` 隔离 DB，与已有显式 STable 路径互不干扰。
  - **全量重跑 24 个基准**（i9-13900HX / .NET 10.0.6 / Docker WSL2，~20 分钟）真实数字写入 `tests/SonnetDB.Benchmarks/README.md`：
    - InsertBenchmark（1M 点）：SonnetDB **544.9 ms / 530 MB**（1.00×）、SQLite 811.4 ms / 465 MB（1.49×）、InfluxDB 5,222 ms / 1,457 MB（**9.58×**）、TDengine REST INSERT 44,137 ms / 156 MB（81×）、**TDengine schemaless LP 996 ms / 61 MB（1.83×）**〔同库 schemaless 路径比 REST INSERT 子表路径快 44× / 分配缩到 39%〕
    - QueryBenchmark：SonnetDB **6.71 ms / 18.7 MB**，比 InfluxDB 快 61×、比 SQLite 快 6.6×
    - AggregateBenchmark：SonnetDB **42.3 ms / 39.4 MB**，比 SQLite 快 7.7×
    - CompactionBenchmark：SonnetDB **16.3 ms / 28.3 MB**
    - **ServerInsertBenchmark（重建镜像后首次全部跑通）**：SQL Batch `19.80 s / 655 MB`、LP `1.293 s / 52 MB`、JSON `1.352 s / 71 MB`、Bulk VALUES `1.120 s / 34 MB`——PR #47 三端点仍稳定在「秒级 1M 点 + ≤ 80 MB」区间，比 SQL Batch 快 **15–7×** / 分配缩到 **5–11%**、仅比嵌入式多 **~2.0–2.5×**额外开销
    - ServerQuery `88.4 ms / 16 MB`、ServerAggregate `88.8 ms / 2.5 MB`、BulkIngestBenchmark 四个路径均保持 PR #46/#47 后「百万点 / ~110–200 ms / 130–220 MB」区间。

### Changed
- 优化查询热路径：未压缩时间戳 block 的范围裁剪改为直接在 little-endian byte payload 上二分，避免为整块 block 分配 `long[]`；数值聚合在无墓碑且非 `First/Last` 时下推到 Segment block payload 扫描，使用 `ReadOnlySpan<byte>` + `CollectionsMarshal.GetValueRefOrAddDefault` 直接更新桶状态，减少 `DataPoint[]` 物化与托管堆分配。

### Added
- **PR #48：批量入库端点 Flush 三档位 `?flush=false|true|async`（写入快路径专题，第 3/4 步）**
  - `Tsdb` 新增 `public void SignalFlush()`：仅向 `BackgroundFlushWorker` 发信号后立即返回，不阻塞调用方；若未启用后台 Flush（`BackgroundFlush.Enabled = false`），降级为同步 `FlushNow()`，保证 `flush=async` 始终具备「最终一致」语义。
  - `BulkIngestor` 新增 `enum BulkFlushMode { None, Async, Sync }` 与新主重载 `Ingest(tsdb, reader, errorPolicy, BulkFlushMode flushMode)`；旧 `Ingest(..., bool flushOnComplete)` 重载保留，转发到新重载（`true → Sync` / `false → None`），向后兼容现有调用方。
  - `BulkIngestEndpointHandler.ParseFlush` 重写为 `BulkFlushMode` 解析：`async` → Async；`true|sync|yes|1` → Sync；其它（含缺省、`false`）→ None。三个端点 `/lp` / `/json` / `/bulk` 共享同一档位。
  - ADO 嵌入式 `EmbeddedConnectionImpl.ExecuteBulk` 新增 `internal static BulkFlushMode ParseFlushMode(string?)` 并改用 `BulkFlushMode` 透传到引擎；远程 `RemoteConnectionImpl.ExecuteBulk` 自然透传 `flush=` query string，无需改动。
  - 默认行为（缺省 `flush` 参数）维持 PR #45 / #47 一致：`BulkFlushMode.None`，仅入 MemTable + WAL，最快路径。
  - 测试：`SonnetDB.Core.Tests` 新增 3 项（Sync/None/Async 三档位的 BulkIngestor 直测，包括 async 不阻塞 < 1s 验证）；`SonnetDB.Tests` 新增 2 项（端点 `?flush=async` / `?flush=false`）。全量回归 `SonnetDB.Core.Tests` 1241/1241 + `SonnetDB.Tests` 97/97 通过。
  - 不修改文件二进制格式与 `FileHeader.Version`。

### Added
- **PR #37：JekyllNet 帮助文档站点 + 镜像内 `/help` 帮助中心**
  - 新增 `.config/dotnet-tools.json`，将 `JekyllNet 0.2.5` 固化为仓库本地工具，统一 `dotnet tool restore` 与 `dotnet tool run jekyllnet build` 的构建入口。
  - `docs/` 现在作为 JekyllNet 站点源目录，新增帮助中心布局、样式以及 `index / getting-started / data-model / sql-reference / file-format / releases` 页面。
  - `SonnetDB` 新增匿名可访问的 `/help/*` 静态文档端点，运行时从 `wwwroot/help` 提供帮助中心，并支持目录式 URL。
  - `src/SonnetDB/Dockerfile` 新增文档构建步骤，在镜像构建时执行 `jekyllnet build --source docs --destination src/SonnetDB/wwwroot/help`，再随 `dotnet publish` 一起打包进镜像。
  - `web/admin` 首页与管理后台头部新增“帮助”入口，直接打开 `/help/`。

### Changed
- 文档：重写仓库 `README.md`，新增 `README.en.md`，并把根 README 调整为当前项目真实形态说明，移除“单文件持久化”与过时目标 API 描述。
- 文档：扩展 `docs/` 帮助中心，新增嵌入式 API、ADO.NET、CLI、批量写入、架构总览页面，并重写首页、快速开始、数据模型、SQL 参考与文件布局说明。
- 文档：修正文档中的实际磁盘布局描述，补充 `measurements.tslschema`、`.system/` 首次安装文件和 `/help` 内置文档站点说明。
- **PR #47：服务端 + Reader 零拷贝（写入快路径专题，第 2/4 步）**
  - `BulkIngestEndpointHandler.ReadAllAsync` 改为返回 `(byte[] Buffer, int Length)`，统一走 `ArrayPool<byte>.Shared.Rent`：已知 `Content-Length` 时按精确长度租借，未知长度则 4KB 起步翻倍扩容；`finally` 必归还，避免大 payload 直入 LOH。
  - `JsonPointsReader` 字段重构为 `ReadOnlyMemory<byte> _utf8Memory + byte[]? _pooledBuffer`：`(ReadOnlyMemory<byte>)` ctor 直接零拷贝持有 caller buffer（原先需 `utf8Json.ToArray()` 全量复制）；`(string)` ctor 走 `ArrayPool<byte>.Shared.Rent` 转码后 Dispose 归还。
  - `BulkIngestEndpointHandler.HandleAsync` JSON 路径直接构造 `new ReadOnlyMemory<byte>(bodyBuffer, 0, bodyLength)` 喂 reader，杜绝 string 中转；LP 路径新增 `CreateLineProtocolReader`，从 `ArrayPool<char>.Shared.Rent` 借出精确长度 char buffer + `Encoding.UTF8.GetChars` 解码后包成 `ReadOnlyMemory<char>`；BulkValues 路径走 `Encoding.UTF8.GetString(buffer, 0, length)` 精确长度版本。`finally` 顺序：dispose reader → return char buffer → return byte buffer。
  - 服务端 `Program.cs` 三个批量端点（`/lp` / `/json` / `/bulk`）追加 `WithMetadata(new DisableRequestSizeLimitAttribute())`，移除 Kestrel 默认 30MB request body 上限，使 1M-row payload 真正可达。
  - 旁路修复：`HelpDocsEndpoints.cs` 集合表达式三元运算符歧义（CS0173）改写为 `new[] { ... }`。
  - **基准复测**（`ServerInsertBenchmark`，1 000 000 点 / i9-13900HX / .NET 10 / Release / `bench-admin-token` 本地 dotnet run）：

    | 路径 | Mean | Allocated | vs PR #45 baseline |
    |------|-----:|----------:|--------------------|
    | `POST /sql/batch` 单行（baseline，PR #45 = 21.36s） | **5.09 s** | 668 MB | **−76% Mean**（受益于 PR #46 真批量） |
    | `POST /v1/db/{db}/measurements/{m}/lp` 1M 点 | **1.20 s** | **52 MB** | **~17.8× faster** / **alloc −92%** |
    | `POST /v1/db/{db}/measurements/{m}/json` 1M 点 | **1.20 s** | **71 MB** | **~17.8× faster** / **alloc −89%** |
    | `POST /v1/db/{db}/measurements/{m}/bulk` 1M 点 | **1.10 s** | **34 MB** | **~19.4× faster** / **alloc −95%** |

    服务端三端点首次进入「秒级 1M 点 + ≤ 80 MB 分配」区间，远超 Milestone 11 既定目标（≥ 700k pts/s）。嵌入式 `BulkIngestBenchmark` 复测无显著变化（该基准不经服务端 handler，对 PR #47 不敏感，符合预期）。
  - 测试：`SonnetDB.Core.Tests` 1237/1238 通过（`BackgroundFlushIntegrationTests.ContinuousWrite_5000Points_AutoFlushesMultipleSegments` 1 处时序敏感 flake，独立跑 2/2 通过）；`SonnetDB.Tests` 95/95 通过。
  - 兼容性说明：`LineProtocolReader` / `BulkValuesReader` / `SchemaBoundBulkValuesReader` 接口保持 `ReadOnlyMemory<char>` / `string`（未做接口级 byte 化），如未来需要彻底 byte-path（避免 LP/Bulk UTF-8→char 一次解码），将作为独立小 PR 推进。

- **PR #46：`Tsdb.WriteMany` 真批量快路径（写入快路径专题，第 1/4 步）**
  - 新增 `Tsdb.WriteMany(ReadOnlySpan<Point>)` 重载：整批写入只获取 **一次** `_writeSync` 锁、批末仅调用 **一次** `BackgroundFlushWorker.Signal`，消除原 `foreach Write(point)` 退化批量在 N 次入锁/信号上的开销。
  - 旧 `Tsdb.WriteMany(IEnumerable<Point>)` 自动嗅探 `Point[]` / `List<Point>` / `ArraySegment<Point>` 并下沉到 span 重载（`CollectionsMarshal.AsSpan` 零拷贝），其它枚举回退到逐点写入；行为对调用方完全透明。
  - WAL 记录格式与 `_walSet` 锁结构 **保持不变**，新旧库双向兼容（`FileHeader.Version` / `TsdbMagic.FormatVersion` 不升）。
  - `BulkIngestor.FlushBatch` 改走 `buffer.AsSpan(0, count)`（替代 `new ArraySegment<Point>(buffer, 0, count)`），消除新重载导致的歧义并直达 span 快路径；`/v1/db/{db}/measurements/{m}/{lp|json|bulk}` 端点与 `RemoteConnectionImpl` 自动受益。
  - **基准复测**（`BulkIngestBenchmark`，100k 点 / i9-13900HX / .NET 10）：

    | 路径 | Mean | Allocated | vs SQL baseline |
    |------|-----:|----------:|-----------------|
    | SQL INSERT VALUES（baseline） | 170 ms | 224 MB | — |
    | TableDirect Line Protocol | 178 ms | 131 MB | **alloc −42%** |
    | TableDirect JSON | 176 ms | 167 MB | alloc −25% |
    | TableDirect Bulk VALUES | 159 ms | 130 MB | **alloc −42%** |

    Mean 与 PR #45 持平（瓶颈仍在 reader 解析 + Catalog/WAL field-level 层），但 **托管堆分配降低 42–58%**（少 N 次 lock entry + iterator boxing），降低 GC 压力。
  - 测试：`SonnetDB.Core.Tests` 1238/1238 通过，`SonnetDB.Tests` 89/90 通过（1 处 pre-existing `UserStore` 并发 IO race 与本 PR 无关）。

### Added
- **SonnetDB 首次安装向导 + 产品首页（未编号）**
  - 新增 `GET /v1/setup/status` 与 `POST /v1/setup/initialize`，当 `<DataRoot>/.system` 未完成初始化时返回首次安装状态，并支持一次性写入服务器 ID、组织、管理员用户名密码与初始 Bearer Token。
  - 新增 `installation.json` 持久化安装元数据；初始 Bearer Token 作为受管用户 token 持久化，后续与现有登录/鉴权体系保持一致。
  - `web/admin` 重构为“产品首页 / 首次安装向导 / 管理后台”三段式路由，新增品牌 logo、帮助导航和首次安装引导页，避免空 `.system` 时直接落到不可登录后台。
- **PR #45：批量入库基准（绕开 SQL 解析的快路径，第 4/4 步）**
  - 新增 `tests/SonnetDB.Benchmarks/Benchmarks/BulkIngestBenchmark.cs`：嵌入式 100 000 点 4 路对比。
    - **baseline**：`SQL INSERT VALUES`（100 行/条 × 1 000 条）走 `SqlParser` + `SqlExecutor.ExecuteInsert` 流水线。
    - **TableDirect Line Protocol**：`LineProtocolReader` + `BulkIngestor.Ingest`。
    - **TableDirect JSON**：`JsonPointsReader` + `BulkIngestor.Ingest`。
    - **TableDirect Bulk VALUES**：`SchemaBoundBulkValuesReader` + `BulkIngestor.Ingest`。
    - 初次运行结果：同量级吞吐，但 LP / Bulk VALUES 内存分配仅为 baseline 的 ~58%，JSON ~75%。
  - `tests/SonnetDB.Benchmarks/Benchmarks/ServerBenchmark.cs::ServerInsertBenchmark` 增加 3 个服务端基准方法：
    - `SonnetDBServer_BulkLp_1M` / `SonnetDBServer_BulkJson_1M` / `SonnetDBServer_BulkValues_1M`，均走 PR #44 新端点 `POST /v1/db/{db}/measurements/{m}/{lp\|json\|bulk}?flush=true`，与原有的 `SonnetDBServer_Insert_1M`（`/sql/batch` SQL 路径）同列对比。
    - LP / JSON / Bulk payload 均在 `[GlobalSetup]` 预生成，不计入迭代耗时；value 统一 `:F4` 格式化避免被 parser 当作 Int64、与 measurement schema (`Float64`) 类型冲突。
  - README 「性能基准」节新增「批量入库快路径（PR #45）」子节：收录 4 路嵌入式对比表与服务端复现命令。

- **PR #44：服务端三端点 + 远程 ADO 客户端打通批量入库（绕开 SQL 解析的快路径，第 3/4 步）**
  - 服务端新增三个端点：
    - `POST /v1/db/{db}/measurements/{m}/lp` —— Line Protocol（`text/plain`）。
    - `POST /v1/db/{db}/measurements/{m}/json` —— JSON points（`application/json`）。
    - `POST /v1/db/{db}/measurements/{m}/bulk` —— `INSERT INTO ... VALUES (...)` 快路径。
    - 三个端点共用 `BulkIngestEndpointHandler`：对 `BulkValues` 走 `SchemaBoundBulkValuesReader`（按 measurement schema 解析列角色），其余直接 new reader → `BulkIngestor.Ingest`。
    - 通过 query string 传参：`?onerror=skip` 切换到 `BulkErrorPolicy.Skip`，`?flush=true` 触发结尾 `Tsdb.FlushNow`。
    - 鉴权：`CanWrite` 才允许（readwrite / admin），与 SQL 端点保持一致；非法 db / measurement 名走 400/404。
    - 响应：`200 OK { "writtenRows": N, "skippedRows": K, "elapsedMilliseconds": ms }`；解析阶段失败 `400 { "error": "bulk_ingest_error", ... }`。
    - 写入行数同步进入 `ServerMetrics.AddInsertedRows`，与 SQL 路径的 `sonnetdb_rows_inserted_total` 计数对齐。
  - `SonnetDB.Data.Remote.RemoteConnectionImpl.ExecuteBulk` 完成实现：
    - 以 `BulkPayloadDetector.DetectWithPrefix` 嗅探协议、切首行 measurement 前缀。
    - measurement 优先级：`cmd.Parameters["measurement"]` > 首行前缀 > JSON `m` 字段 > `INSERT INTO <name>`。
    - 选择 `lp / json / bulk` 端点；`onerror`、`flush` 透传为 query string；payload 直接以 `text/plain`（LP / Bulk）或 `application/json`（JSON）POST。
    - 解析 `BulkIngestResponseBody.WrittenRows` 后包成 `MaterializedExecutionResult.NonQuery`。
  - 抽取共用 `SonnetDB.Ingest.SchemaBoundBulkValuesReader.Create(tsdb, sql, measurement)` 工厂：把 `tsdb.Measurements` 的 schema 桥接到 `BulkValuesReader` 的列角色 resolver。`EmbeddedConnectionImpl` 与服务端共用此工厂，避免逻辑重复。
  - 新增 `BulkIngestResponse`（服务端契约）、`BulkIngestResponseBody`（远程客户端 DTO）；两端均纳入 source-gen JSON context（AOT 友好）。
  - 测试：`tests/SonnetDB.Tests/BulkIngestEndpointTests.cs`（10 用例：LP/JSON/Bulk × {成功 / onerror=skip / FailFast 400 / flush=true / RBAC / 404 / 401 / 写后 SELECT 回查}）+ `RemoteAdoBulkIngestTests.cs`（7 用例：远程 ADO `CommandType.TableDirect` 三协议 + measurement 参数 / onerror / 403 / 缺 measurement 抛 InvalidOperationException）。

- **PR #43：`SonnetDB.Data` 接入批量入库快路径（绕开 SQL 解析的快路径，第 2/4 步）**
  - 扩展 `IConnectionImpl` 增加 `ExecuteBulk(commandText, parameters)`，与 `Execute(sql, …)` 并列。
  - `SndbCommand.CommandType` 由只读 `Text` 改为可读写字段，允许 `CommandType.Text`（默认）与 `CommandType.TableDirect`；其它值（如 `StoredProcedure`）仍抛 `NotSupportedException`。
  - `SndbCommand.ExecuteCore` 在 `TableDirect` 下跳过 `ParameterBinder`，把 `CommandText` 整段交给 `IConnectionImpl.ExecuteBulk`。
  - `EmbeddedConnectionImpl.ExecuteBulk` 桥接 `SonnetDB.Ingest`：用 `BulkPayloadDetector.DetectWithPrefix` 嗅探协议并切首行 measurement 前缀；按 `LineProtocol`/`Json`/`BulkValues` 路由到对应 reader；对 `BulkValuesReader` 通过闭包从 `Tsdb.Measurements.TryGet(measurement).TryGetColumn(col).Role` 解析列角色（Tag/Field/Time）；最终经 `BulkIngestor.Ingest` 写入并把写入行数包成 `MaterializedExecutionResult.NonQuery`。
  - 命令参数 hooks：`measurement`（覆盖 payload 内的 measurement）、`onerror=skip`（切换到 `BulkErrorPolicy.Skip`）、`flush=true`（写入完成后触发 `Tsdb.FlushNow`）；参数名兼容 `@`/`:` 前缀。
  - `RemoteConnectionImpl.ExecuteBulk` 占位实现：抛 `NotSupportedException`，明确指向 PR #44。
  - 新增 `tests/SonnetDB.Core.Tests/Ado/TsdbBulkIngestAdoTests.cs`：10 个 xUnit 用例覆盖 LP / JSON / BulkValues / 首行前缀 / 参数（onerror/flush/measurement）/ 未知列 / `StoredProcedure` 拒绝 / TableDirect 与普通 SELECT 在同一连接共存等场景。
  - 兼容性：现有 `CommandType.Text` 路径行为完全不变；不引入新依赖。

- **PR #A：批量入库核心 `SonnetDB.Ingest` 命名空间（绕开 SQL 解析的快路径，第 1/4 步）**
  - 新增 `src/SonnetDB/Ingest/BulkPayloadFormat.cs`：`BulkPayloadFormat` 枚举（`LineProtocol` / `Json` / `BulkValues`）+ `TimePrecision`（`Nanoseconds` / `Microseconds` / `Milliseconds` / `Seconds`）。
  - 新增 `src/SonnetDB/Ingest/BulkPayloadDetector.cs`：O(1) 前后字节嗅探协议；`DetectWithPrefix` 支持可选首行 measurement 前缀（首行不含空白/`=`/`,`/`{}`/`()`/`;` 时视为 measurement）。
  - 新增 `src/SonnetDB/Ingest/IPointReader.cs`（与 `LineProtocolReader.cs` 同文件）：流式 `TryRead(out Point)` 通用契约。
  - 新增 `src/SonnetDB/Ingest/LineProtocolReader.cs`：InfluxDB Line Protocol 子集（`double` / `42i` / `t`/`f`/`true`/`false` / `"…"` field；`\,` `\=` `\空格` `\\` 转义；ns/us/ms/s 精度换算；`measurementOverride`；空行与 `#` 注释跳过）。
  - 新增 `src/SonnetDB/Ingest/JsonPointsReader.cs`：基于 `Utf8JsonReader` 的流式 JSON reader，schema `{"m":"…","precision":"ms","points":[{"t":…,"tags":{…},"fields":{…}}]}`，避免一次性反序列化大 payload；支持 `measurementOverride` 与单点级 `measurement` 覆盖。
  - 新增 `src/SonnetDB/Ingest/BulkValuesReader.cs`：`INSERT INTO m(cols) VALUES (…),(…),…;` 形式的快路径 reader；表头按需解析一次后，VALUES 走自写扫描器（支持单引号字符串 + `''` 转义 / 整数 / 浮点 / `TRUE`/`FALSE`/`NULL` / 双引号或反引号包裹的标识符）；列角色由调用方 `Func<string, BulkValuesColumnRole>` resolver 提供，便于与 measurement schema 解耦。
  - 新增 `src/SonnetDB/Ingest/BulkIngestor.cs`：统一消费入口；`ArrayPool<Point>` 8192 批 → `Tsdb.WriteMany`；支持 `BulkErrorPolicy.FailFast` / `Skip` 与可选 `flushOnComplete`；返回 `BulkIngestResult(Written, Skipped)`。
  - 新增 `src/SonnetDB/Ingest/BulkIngestException.cs`：批量入库专用异常类型。
  - 新增 `tests/SonnetDB.Core.Tests/Ingest/` 下 5 个测试类（`BulkPayloadDetectorTests` / `LineProtocolReaderTests` / `JsonPointsReaderTests` / `BulkValuesReaderTests` / `BulkIngestorTests`），共 38 个 xUnit 用例覆盖：协议嗅探、首行前缀切分、LP 与 JSON 与 Bulk INSERT VALUES 解析与异常路径、`BulkIngestor` 在 batch 边界（>8192 行）下的正确性、`Skip` 策略与 `flushOnComplete` 路径。
  - 仍保持 `src/SonnetDB` 零第三方运行时依赖（仅 `System.IO.Hashing`），不引入新的 NuGet 包。

- **PR #38：发布 `SonnetDB 0.1.0` 的 NuGet、二进制包、完整服务端包与安装包**
  - 新增 `src/SonnetDB/PackageReadme.md`、`src/SonnetDB.Data/PackageReadme.md`、`src/SonnetDB.Cli/PackageReadme.md`，并在三个项目文件中补齐 `PackageId`、`PackageReadmeFile`、版本元数据，支持直接生成 `SonnetDB`、`SonnetDB.Data`、`SonnetDB.Cli` 三个 `0.1.0` 包。
  - `SonnetDB.Cli` 从占位程序升级为可用命令行工具：支持 `sndb version`、`sndb sql --connection ... --command|--file ...` 和 `sndb repl --connection ...`，可直接连接本地嵌入式数据库或远程 `SonnetDB`。
  - 新增 `tests/SonnetDB.Core.Tests/Cli/CliApplicationTests.cs`，覆盖 CLI 帮助输出与本地 SQL 执行回归场景。
  - 新增 `src/SonnetDB.Data/Internal/ExecutionFieldTypeResolver.cs`，并为 `SndbDataReader` / `IExecutionResult` / `MaterializedExecutionResult` / `RemoteExecutionResult` 补齐 trim/AOT 注解与显式类型映射，保持 `SonnetDB.Cli` 接入 `SonnetDB.Data` 后仍可 `PublishAot=true` 通过发布。
  - 新增 `docs/releases/README.md`、`docs/releases/sdk-bundle.md`、`docs/releases/server-bundle.md`、`docs/releases/installers.md`，说明 NuGet 包、SDK Bundle、Server Full Bundle、MSI/DEB/RPM 安装包的用途、目录结构、默认启动方式与凭据。
  - 新增跨平台发布脚本 `eng/release.ps1`：
    - 生成 `SonnetDB` / `SonnetDB.Data` / `SonnetDB.Cli` NuGet 包；
    - 发布 Windows / Linux 原生 AOT CLI 与 Server；
    - 生成 `sndb-sdk-<version>-<rid>` 与 `sonnetdb-full-<version>-<rid>` 压缩包；
    - 自动写入默认本地启动配置、预置管理员 `admin / Admin123!` 与 Bearer Token `sonnetdb-admin-token`；
    - 生成 SHA256 校验文件；
    - 生成 Windows `msi` 与 Linux `deb` / `rpm` 安装包。
  - 新增 `.github/workflows/publish.yml`，在 `v*` tag 或手动触发时自动执行：
    - NuGet 打包与发布；
    - Windows / Linux Server + CLI + 前端完整打包；
    - MSI / DEB / RPM 安装包构建；
    - GitHub Release 附件上传。
- **PR #35：BenchmarkDotNet 五库性能基准全量收敛**
  - 所有基准代码与 docker compose 编排在前序 PR（#32 / #33 / #36 工作流）中已陆续落地，PR #35 收敛验收并将 ROADMAP 状态置 ✅。
  - 五个数据库的覆盖矩阵（详见 README「五库基准覆盖一览」）：
    - **SonnetDB（嵌入式）**：写入 + 范围查询 + 聚合 + Compaction（4→1 段合并）。
    - **SQLite**（`Microsoft.Data.Sqlite`，WAL）：写入 + 范围查询 + 聚合。
    - **InfluxDB 2.7**（HTTP Line Protocol + Flux）：写入 + 范围查询 + 聚合。
    - **TDengine 3.3**（REST + 超级表/子表）：写入 + 范围查询 + 聚合。
    - **SonnetDB**（HTTP Batch SQL + ndjson）：写入 + 范围查询 + 聚合。
  - 数据规模统一 100 万点、单序列 `host=server001`、`value DOUBLE`，外部数据库不可用时各基准独立 `[SKIP]`。
  - README 新增「五库基准覆盖一览」表，明确标注 Compaction 仅适用于 SonnetDB，并指向各详细结果章节；同时补 ROADMAP Milestone 9 推进顺序为 PR #37 → PR #38 → PR #39。
- **SonnetDB 实时事件流：SSE + 前端订阅自动刷新（PR #34c）**
  - 服务端新增 `src/SonnetDB/Hosting/EventBroadcaster.cs`：基于 `System.Threading.Channels` 的多路广播器（`BoundedChannel` + `FullMode.DropOldest`，容量 64），按通道 `metrics` / `slow_query` / `db` 过滤订阅，慢消费者自动丢最旧帧不阻塞 publish。
  - 新增 `src/SonnetDB/Endpoints/SseEndpointHandler.cs`：实现 `GET /v1/events`，响应 `text/event-stream`，禁用 buffering（`X-Accel-Buffering: no`），按 SSE 帧格式输出 `event:` + `id:` + `data:` 三行；30 秒空闲发 `: heartbeat` 注释行心跳；支持 `?stream=metrics,slow_query,db` 通道筛选；连接建立先发 `hello` 帧。
  - 新增 `src/SonnetDB/Hosting/MetricsTickService.cs`：`BackgroundService` + `PeriodicTimer`，按 `MetricsTickSeconds`（默认 5s）周期生成 `MetricsSnapshotEvent`（含数据库数 / 用户数 / 订阅者数 / 各库 segment 计数），仅在有订阅者时构造 + 推送，避免无人订阅时空转。
  - 新增 `src/SonnetDB/Contracts/Events.cs`：`ServerEvent` / `MetricsSnapshotEvent` / `SlowQueryEvent` / `DatabaseEvent` DTO（DTO 入 `ServerJsonContext` source-gen 满足 AOT），通道常量 `ChannelMetrics` / `ChannelSlowQuery` / `ChannelDatabase`。
  - `Configuration/ServerOptions.cs` 新增 `SlowQueryThresholdMs`（默认 500ms）/ `MetricsTickSeconds`（默认 5s）。
  - `Endpoints/SqlEndpointHandler.cs`：`HandleSingleAsync` / `HandleBatchAsync` 增加 `string databaseName` 参数；新增 `MaybePublishSlow` helper 在所有路径（成功 / 失败 / 控制面 / 单条 / 批量）统计耗时，超阈值即广播 `SlowQueryEvent`（控制面用 `__control` 标签，SQL 截断到 1024 字节）。
  - `Hosting/TsdbRegistry.cs`：构造器接受 `EventBroadcaster?`，`TryCreate` / `Drop` 成功后广播 `DatabaseEvent`（`created` / `dropped`）。
  - `Auth/BearerAuthMiddleware.cs`：当请求路径是 `/v1/events` 且无 `Authorization` 头时，从 `?access_token=<tok>` query 取 token，因为浏览器 `EventSource` API 无法发自定义 header。
  - 前端新增 `web/admin/src/api/events.ts`：`subscribeServerEvents(token, opts)` 封装 `EventSource`，挂载 `hello` / `metrics` / `slow_query` / `db` 监听并把 401 / `EventSource.CLOSED` 标记为 `unauthorized` 状态，返回关闭函数。
  - 前端新增 `web/admin/src/stores/events.ts`：Pinia store，缓冲最近 100 条慢查询 + 100 条 db 事件，维护 `dbEventBumper` 计数信号 + 当前 `metrics` 快照，监听 `auth.isAuthenticated` 自动 connect / disconnect。
  - 前端新增 `web/admin/src/views/EventsView.vue`：实时指标 grid（8 个 statistic）+ 慢查询表（带成功/失败 tag）+ 数据库事件表（带 created/dropped tag）。
  - `views/AppShell.vue` 顶栏增加 SSE 状态指示（n-tag + 状态点 CSS）+ 新增「事件流」菜单项；`router/index.ts` 注册 `/admin/events` 路由。
  - `views/DashboardView.vue` / `views/DatabasesView.vue` 各 `watch(() => events.dbEventBumper, reload)` + `watch(() => events.metrics, ...)`，CREATE/DROP DATABASE 在所有打开的客户端无需手动刷新即可即时同步；Dashboard 的 segment 计数也由 metrics 帧覆盖。
  - 测试：新增 `tests/SonnetDB.Tests/SseEndToEndTests.cs` 端到端覆盖：401 拒绝匿名访问、`?access_token=` query 鉴权通过、收到 `hello` → CREATE → `db.created` → 触发 SQL → `slow_query`（阈值压到 0）→ 周期性 `metrics`（tick 设 1s）→ DROP → `db.dropped` 完整链路。
  - `web/admin/tsconfig.json` 把 `ignoreDeprecations` 从 `"6.0"` 调整为 `"5.0"`，使本地 TypeScript 5.6.x 可继续构建；语义不变（仍是抑制 `baseUrl` 废弃告警）。
- **SonnetDB Admin Vue3 管理后台完成（PR #34b）**
  - `web/admin/`：Vite + Vue 3 + TypeScript + Naive UI + Pinia + Vue Router 单页应用，完整涵盖登录页、数据库列表/状态、SQL 控制台、用户/权限/Token 管理七个视图：
    - `LoginView.vue`：Bearer token 登录，结果存 localStorage，axios 拦截器自动注入 `Authorization: Bearer`。
    - `AppShell.vue`：响应式侧边栏 + 顶栏（用户名 / 角色标签 / 退出），超级用户额外显示用户 / 权限 / Token 菜单，路由 Guard 拦截未登录与越权访问。
    - `DashboardView.vue`：数据库数量 / 用户数量 / 授权条目三个统计卡，数据库状态表格（在线状态 + Segment 数），admin 额外展示用户列表。
    - `DatabasesView.vue`：`GET /v1/db` + `/metrics` 展示数据库列表/Segment 数，admin 可创建（`CREATE DATABASE`）与二次确认 DROP。
    - `SqlConsoleView.vue`：目标数据库选择器（admin 额外有控制面选项）+ 多行 SQL 编辑器 + 运行/清空按钮 + ndjson 结果表格 + 行数/耗时 meta 行。
    - `UsersView.vue`（admin only）：`SHOW USERS` 列表、`CREATE USER ... [SUPERUSER]` 表单、改密弹窗（`ALTER USER`）、二次确认 DROP。
    - `TokensView.vue`（admin only）：`SHOW TOKENS [FOR user]` 列表、`ISSUE TOKEN FOR <user>` 签发并弹窗展示明文、按用户筛选、行级 `REVOKE TOKEN`。
  - 服务端嵌入资源管线：`AdminUiAssets` / `AdminUiEndpoints` 把 `web/admin/dist/**` 以 `EmbeddedResource` 嵌入 dll，运行时按路径提供文件，SPA 路由 fallback 到 `index.html`，hash 化资产设 `immutable` 缓存，dist 未 build 时返回 503 提示。
  - 专用控制面 SQL 端点 `POST /v1/sql`（admin only）运行控制面语句（CREATE USER / GRANT / SHOW USERS …），数据面 SQL 走 `POST /v1/db/{db}/sql`；前端通过 `execControlPlaneSql` / `execDataSql` 统一调用。
  - `tsconfig.json` 添加 `"ignoreDeprecations": "6.0"` 抑制 TypeScript 对 `baseUrl` 的废弃警告（TS5101：`baseUrl` 将在 TypeScript 7.0 停止工作）。
- **SonnetDB Docker 性能测试（PR #36）**
  - 新增 `src/SonnetDB/Dockerfile`：基于 `mcr.microsoft.com/dotnet/sdk:10.0` 多阶段构建，最终镜像基于 `mcr.microsoft.com/dotnet/aspnet:10.0`，框架依赖发布（Framework-dependent）。
  - 更新 `tests/SonnetDB.Benchmarks/docker/docker-compose.yml`：新增 `sonnetdb` 服务，暴露端口 5080，默认 token `bench-admin-token`，含健康检查（wget `/healthz`）和 Docker Volume 持久化。
  - 新增 `tests/SonnetDB.Benchmarks/Benchmarks/ServerBenchmark.cs`：包含 `ServerInsertBenchmark`、`ServerQueryBenchmark`、`ServerAggregateBenchmark` 三个基准类，通过 `HttpClient` 直接调用 SonnetDB HTTP API（Batch SQL insert / SELECT / GROUP BY 聚合），服务不可用时自动 `[SKIP]`。
  - 更新 `README.md`：新增「SonnetDB 服务器模式性能基准」章节，记录 Docker 容器测试环境（AMD EPYC 9V74，Ubuntu 24.04，.NET 10.0.5）及实测结果：写入 13.16 s（HTTP Batch 2k/批）、范围查询 210.8 ms、1 分钟桶聚合 138.3 ms，并对比嵌入式模式额外开销。
- **SonnetDB Admin SPA：数据库状态 + Token 管理（PR #34b-4）**
  - 前端新增 `web/admin/src/views/TokensView.vue`，提供 admin-only 的 Token 管理页：`SHOW TOKENS [FOR user]` 列表、`ISSUE TOKEN FOR <user>` 一次性签发明文 token、`REVOKE TOKEN '<tokenId>'` 行级吊销，并在弹窗中提示“token 明文只展示一次”。
  - `web/admin/src/router/index.ts` / `views/AppShell.vue` 新增 `tokens` 路由与侧边栏菜单；用户 / 权限 / Token 三个控制面页面现在形成完整闭环。
  - `UserStore` / `ControlPlane` / `SqlExecutor` 的 token 查询与吊销链路补齐单元/集成/E2E 覆盖：新增 `ListTokensDetailed` + `RevokeTokenById` 验证、控制面 SQL 的 `ISSUE/SHOW/REVOKE TOKEN` round-trip，以及 token 吊销后旧 Bearer 立即失效的端到端断言。
- **SonnetDB 控制面：用户 / 权限 / 数据库 DDL（PR #34a）**
  - 新增持久化用户与权限存储（仅服务端）：`src/SonnetDB/Auth/UserStore.cs` + `GrantsStore.cs`，文件落 `<DataRoot>/.system/{users.json,grants.json}`，原子写入（temp + `Flush(true)` + `File.Move(overwrite=true)`）。
  - 密码：PBKDF2-HMAC-SHA256，100 000 轮、16 字节随机 salt、32 字节 hash；校验用 `CryptographicOperations.FixedTimeEquals` 防侧信道。
  - 动态 API token：32 字节随机（`RandomNumberGenerator`）→ Base64Url，仅存 SHA-256 hex 哈希；token id 形如 `tok_<8hex>`，便于审计与单条吊销。
  - 权限模型：`enum DatabasePermission { Read=1, Write=2, Admin=3 }`，按 `(database,user)` 单条记录；`*` 通配整个集群。
  - **SQL 控制面 DDL**：在 `src/SonnetDB/Sql/` 新增 7 条语句类型 + parser 分支（`ParseStatement` 添加 `Drop`/`Alter`/`Grant`/`Revoke`，新增 `ParseCreate` 二级分发）：
    - `CREATE USER <name> WITH PASSWORD '<pwd>' [SUPERUSER]`
    - `ALTER USER <name> WITH PASSWORD '<new>'`（成功后吊销该用户全部旧 token）
    - `DROP USER <name>`（级联删除其所有 grants）
    - `GRANT READ|WRITE|ADMIN ON DATABASE <db|*> TO <user>`
    - `REVOKE ON DATABASE <db|*> FROM <user>`
    - `CREATE DATABASE <name>` / `DROP DATABASE <name>`（通过 `IControlPlane` 触发 `TsdbRegistry.TryCreate/Drop`，并级联 grants）
  - **执行层 `IControlPlane`**：`src/SonnetDB/Sql/Execution/IControlPlane.cs` 在核心库声明（零依赖），`SqlExecutor.ExecuteStatement` 新增 `IControlPlane?` 参数；嵌入式连接传入 `null` → 控制面 DDL 抛 `NotSupportedException`（"控制面 DDL（CREATE USER / GRANT / CREATE DATABASE 等）仅在服务端模式可用。"）。服务端在 `src/SonnetDB/Auth/ControlPlane.cs` 提供桥接实现：用户/权限/数据库三向级联（DROP USER → DeleteUserGrants，DROP DATABASE → DeleteDatabaseGrants）。
  - **认证扩展（PR #34a-5）**：`BearerAuthMiddleware.Authenticate` 新增 `UserStore?` 入参，先匹配 `ServerOptions.Tokens` 静态映射，未命中再走 `UserStore.TryAuthenticate`（哈希查表）；命中动态 token 时把 `AuthenticatedUser` 写入 `HttpContext.Items["sndb.user"]`，超级用户映射 `admin` 角色，普通用户映射 `readwrite`。`/v1/auth/login` 路径始终匿名。
  - **新端点 `POST /v1/auth/login`**：接收 `{username,password}`，PBKDF2 校验通过后调用 `UserStore.IssueToken` 颁发新 token，返回 `{username,token,tokenId,isSuperuser}`。⚠️ 该端点用 `app.MapMethods(path, ["POST"], (RequestDelegate)(async ctx => ...))` 直接以 `RequestDelegate` 形式注册（详见 `Fixed`）。
  - **SQL 端点权限收紧**：`src/SonnetDB/Endpoints/SqlEndpointHandler.cs` 在执行前 parse 一次，识别为控制面 DDL 时要求 `isAdmin == true`，否则返回 `forbidden`；写操作（INSERT/DELETE）仍按 `canWrite` 判定。批处理同步收紧。
  - **测试**：5 个端到端用例 `tests/SonnetDB.Tests/AuthControlPlaneEndToEndTests.cs` 覆盖：登录字段缺失 → 400、未知用户 → 401、CREATE USER + GRANT + 登录拿 token + 用 token 调 `/healthz` 与 SQL 端点（普通用户控制面 DDL → forbidden）、动态非 admin token 控制面 DDL → forbidden、ALTER USER 改密后旧 token 立即失效 → 401。服务端测试总数：49 通过 / 0 失败。
- **SonnetDB 控制面查询 SQL：SHOW USERS / GRANTS / DATABASES（PR #34b-1）**
  - 新增 SQL 关键字 `SHOW / USERS / GRANTS / DATABASES / FOR`（`src/SonnetDB/Sql/TokenKind.cs` + `SqlLexer.cs`）。
  - 新增 AST：`ShowUsersStatement` / `ShowGrantsStatement(UserName)` / `ShowDatabasesStatement`，`SqlParser.ParseShow()` 分发；`SHOW GRANTS [FOR <user>]` 中 `FOR` 子句可选。
  - `IControlPlane` 扩展 3 个查询方法：`ListUsers()` → `IReadOnlyList<UserSummary>`、`ListGrants(string?)` → `IReadOnlyList<GrantSummary>`、`ListDatabases()` → `IReadOnlyList<string>`；`UserSummary(Name, IsSuperuser, CreatedUtc, TokenCount)` 与 `GrantSummary(UserName, Database, Permission)` 在核心库声明。
  - `SqlExecutor` 把 SHOW 语句包装成 `SelectExecutionResult`，复用现有 `/v1/db/{db}/sql` ndjson 渲染管线，无需新增 REST 端点。
  - 服务端 `UserStore.ListUsersDetailed()` 与 `GrantsStore.ListAll()` 提供枚举支撑；`ControlPlane` 用反向权限映射 (`MapPermissionBack`) 把 `DatabasePermission` 转回 `GrantPermission`。
  - 权限收紧：`SHOW USERS` / `SHOW GRANTS` 在 `SqlEndpointHandler.IsControlPlaneStatement` 中归为 admin-only；`SHOW DATABASES` 任何已认证用户均可执行。
  - 测试：parser 5 例（`Parse_ShowUsers/ShowDatabases/ShowGrants_NoFilter/WithFor` + 3 个 bad grammar Theory）+ ControlPlane 集成 3 例（`ListUsers_ReturnsCreatedUsersOrderedByName` / `ListGrants_NullFilter_ReturnsAll` / `ListDatabases_ReflectsRegistry`）+ E2E 3 例（`ShowUsers_AsAdmin_ReturnsRows` / `ShowUsers_AsRegularUser_Forbidden` / `ShowDatabases_AsAdmin_ReturnsRows`）。当前测试总数：1174 + 55 = 1229 全绿。
- **SonnetDB Admin SPA 脚手架：嵌入式静态资源管线（PR #34b-2）**
  - 新增 `web/admin/` 完整 Vite + Vue 3 + TypeScript + Naive UI + Pinia + Vue Router 单页应用脚手架，含 `LoginView`（PBKDF2 登录 → token 存 localStorage）+ `DashboardView`（数据库 / 用户 / grants 概览，**全部走 SHOW SQL**，零额外 REST 端点）。
  - 路由前缀固定为 `/admin/`；axios 拦截器自动注入 `Bearer <token>`；vite dev 反代 `/v1`、`/healthz`、`/metrics` → `:5000`。
  - 服务端 `src/SonnetDB/Hosting/AdminUiAssets.cs`：启动时一次性把 `web/admin/dist/**` 嵌入资源（`sndb.admin/...`）加载到 `FrozenDictionary`，AOT 友好的 MIME 类型 switch；`AdminUiEndpoints.MapAdminUi()` 注册 `GET /admin` 与 `GET /admin/{**path}`，命中具体文件返回原字节，未命中且无扩展名时回退 `index.html`（SPA 客户端路由），manifest 为空时返回 503 + 提示 `npm run build`。
  - `BearerAuthMiddleware.Authenticate` 豁免 `/admin/*` 路径匿名访问（仅静态资源；任何管理动作仍需登录后凭 token 调 `/v1/db/{db}/sql`）。
  - csproj 集成：`web/admin/dist/**` 通过 `<EmbeddedResource>` 自动嵌入，`LogicalName` 写为 `sndb.admin/%(RecursiveDir)%(Filename)%(Extension)`，C# 端把 `\` 规范化为 `/`；可选 target `BuildAdminUi=true` 自动跑 `npm install && npm run build`（默认 false，避免日常 `dotnet build` 被 npm 拖慢）。dist 目录通过 `web/admin/.gitignore` 排除，不入库。
  - 缓存策略：`/admin`、`/admin/index.html` → `no-cache`；其他 hash 化资产 → `public, max-age=31536000, immutable`（与 Vite 默认 contenthash 命名匹配）。
  - 测试：6 个端到端用例 `tests/SonnetDB.Tests/AdminUiEndToEndTests.cs` 覆盖 `GET /admin` 返回 HTML、SPA fallback (`/admin/login` → index.html)、带扩展名缺失 → 404、匿名可访问、favicon → image/svg+xml、`/v1/db` 仍要求 Bearer。当 dist 未 build 时所有用例自动跳过断言（CI 友好）。当前测试总数：1174 + 61 = **1235 全绿**。
- **SonnetDB Admin SPA：SQL Console + 数据库 / 用户 / 权限管理（PR #34b-3）**
  - 新增专用控制面 SQL 端点 `POST /v1/sql`（admin only，无 db 路径），仅接收控制面语句（CREATE USER / GRANT / CREATE DATABASE / SHOW USERS / SHOW DATABASES 等）；数据面语句 → 400。仍然以 `application/x-ndjson` 流式输出，行格式与 `/v1/db/{db}/sql` 完全一致，前端可共享解析器。复用 `app.MapMethods(path, ["POST"], (RequestDelegate)(...))` 模式绕过 AOT RequestDelegateGenerator 拦截。
  - 核心库新增 `SqlExecutor.ExecuteControlPlaneStatement(SqlStatement, IControlPlane)` 入口，独立于 `Tsdb` 实例运行；用于 `/v1/sql` 端点，使前端无需先选数据库就能跑控制面 SQL。
  - **SQL grammar 补全 `SUPERUSER` 关键字**：`CREATE USER <name> WITH PASSWORD '<pwd>' [SUPERUSER]` 末尾可选关键字现在被解析（此前 `IsSuperuser` 始终为 `false`）。新增 `TokenKind.KeywordSuperuser` + lexer 映射 + parser 可选消费 + 2 个 parser 单测。
  - **前端 SPA 重构**：抽出 `web/admin/src/api/sql.ts` 共享 ndjson 解析（`parseNdjson` / `execControlPlaneSql` / `execDataSql` / `rowsToObjects` / `quote` / `isValidIdentifier`），所有视图共用 `axios.validateStatus:()=>true` + `responseType:'text'` 模式正确处理 4xx / 5xx 响应。
  - 抽出 `views/AppShell.vue` 共享布局壳子（sider + header + `<router-view/>`），由 `App.vue` 顶层挂载 `NDialogProvider / NMessageProvider / NNotificationProvider`；普通用户菜单显示「概览 / SQL Console / 数据库」，超级用户额外显示「用户 / 权限」。
  - 4 个新视图：
    - `SqlConsoleView.vue`：目标选择器（控制面 / 任意数据库）+ 多行 SQL 编辑 + 运行按钮 + 结果表格 + meta（行数 / 受影响行数 / elapsedMs）+ error alert。
    - `DatabasesView.vue`：`SHOW DATABASES` 列表 + admin 创建（`CREATE DATABASE`，标识符校验）+ 二次确认 DROP。
    - `UsersView.vue`（admin only）：`SHOW USERS` 表格 + `CREATE USER ... [SUPERUSER]` 表单 + 改密弹窗（`ALTER USER ... WITH PASSWORD '...'`）+ 二次确认 DROP。
    - `GrantsView.vue`（admin only）：`SHOW GRANTS` 表格 + `GRANT READ|WRITE|ADMIN ON DATABASE <db> TO <user>` 表单 + 行级 `REVOKE ON DATABASE <db> FROM <user>` 二次确认。
  - 路由结构调整：`/admin/login` 匿名；其余路由全部嵌套在 `AppShell` 子路由下（`/dashboard` / `/sql` / `/databases` / `/users` / `/grants`），全局 guard 增加 `meta.admin` 判断（非 admin 访问 admin 路由 → 重定向 `/dashboard`）。
  - 测试：parser 新增 2 例（`Parse_CreateUser_WithSuperuserKeyword_SetsFlag` / `Parse_CreateUser_SuperuserKeyword_CaseInsensitive`）+ 服务端 E2E 新增 4 例（`ControlPlaneEndpoint_AsAdmin_RunsCreateUserAndShowUsers` / `ControlPlaneEndpoint_CreateSuperuser_FlagPersisted` / `ControlPlaneEndpoint_AsRegularUser_Forbidden` / `ControlPlaneEndpoint_RejectsDataPlaneStatement`）。当前测试总数：1176 + 65 = **1241 全绿**。

### Fixed
- **Admin SPA 普通用户数据库列表修正（PR #34b-4）**
  - `DashboardView` / `DatabasesView` / `SqlConsoleView` 不再错误地把 `SHOW DATABASES` 走到 admin-only 的 `POST /v1/sql`；数据库列表改走 `GET /v1/db`，数据库状态通过 `GET /metrics` 读取 `sonnetdb_segments{db="..."}`，因此普通已登录用户也能查看数据库概览与段数量。
  - `SqlConsoleView` 的“控制面”目标选择现在仅对 admin 显示；普通用户默认落到首个可访问数据库，避免一进控制台就遇到 `/v1/sql 仅 admin 可调用` 的无意义报错。
- **AOT RequestDelegateGenerator workaround**：`WebApplication.CreateSlimBuilder` + `EnableRequestDelegateGenerator=true` 下，对于
  `app.MapPost(path, async (HttpContext ctx) => Results.Json(value, typeInfo, statusCode: 4xx))` 形态的 lambda，生成的 interceptor 会
  错误地把响应吞成 `200 + 空 body`（lambda 实际执行，但 statusCode 与 body 全部丢失）。`/v1/auth/login` 改为
  `app.MapMethods(path, ["POST"], (RequestDelegate)(async ctx => { ctx.Response.StatusCode = ...; await JsonSerializer.SerializeAsync(...); }))`
  绕过 generator 拦截，行为稳定（PR #34a）。

- **SonnetDB.Data**：将 ADO.NET API 从 `SonnetDB` 核心库剥离为独立的 `src/SonnetDB.Data/`（PR #33）
  - 公共表面保持兼容：`SndbConnection` / `SndbCommand` / `SndbDataReader` / `SndbParameter` / `SndbParameterCollection` / `SndbConnectionStringBuilder` 命名空间从 `SonnetDB.Ado` 迁移到 `SonnetDB.Data`；`src/SonnetDB/Ado/` 目录整体删除。
  - **嵌入式 + 远程双模式**：通过连接字符串 scheme 自动分派，由内部接口 `IConnectionImpl` 统一抽象。
    - 嵌入式：`Data Source=<path>` 或 `sonnetdb://<path>` → `EmbeddedConnectionImpl` 复用 `SharedSndbRegistry` + 进程内 `Tsdb`，行为与原 `SonnetDB.Ado` 完全一致。
    - 远程：`Data Source=sonnetdb+http://host:port/<db>;Token=<bearer>` 或 `http(s)://...` → `RemoteConnectionImpl` 通过 `HttpClient` + ndjson 流式协议直连 `SonnetDB` 的 `POST /v1/db/{db}/sql` 端点；服务端错误抛 `SndbServerException`（含 `Error` / `ServerMessage` / `StatusCode`）。
    - `SndbConnectionStringBuilder.ResolveMode()` 支持显式 `Mode=Embedded|Remote` 覆盖；新增 `Token` / `Timeout`（默认 100s）键。
    - `SndbConnection.ProviderMode` 暴露当前模式；`UnderlyingTsdb` 仅在嵌入式模式返回非空。
  - 新增 `SndbProviderFactory : DbProviderFactory`（单例 `Instance`），可注册到 `DbProviderFactories` 供通用 ADO 工具使用。
  - `IsAotCompatible=false` 并附详细注释（理由：`DbConnection` / `DbCommand` 基类大量反射；主流 ADO 提供程序如 Npgsql、MySqlConnector 也未承诺 AOT；需要 AOT 的场景请直接使用 `Tsdb` API）。
  - 远程客户端 ndjson 解析使用 `System.Text.Json` 源生成器（`RemoteJsonContext`）+ `JsonDocument` 解析行级数组，`HttpCompletionOption.ResponseHeadersRead` 实现真流式读取。
  - 9 个端到端测试（`tests/SonnetDB.Tests/RemoteAdoEndToEndTests.cs`）覆盖：scheme 分派、嵌入式回退、CREATE→INSERT→SELECT 全链路、参数绑定与单引号转义、`ExecuteScalar`、只读令牌 INSERT 拒绝、SQL 错误、缺失令牌 401、未知数据库 404；既有 31 个 `TsdbAdoApiTests` 全部保持通过。

- **SonnetDB**：Native AOT 友好的 Minimal API HTTP 服务器（PR #32）
  - 新项目 `src/SonnetDB/`，基于 `Microsoft.NET.Sdk.Web` + `WebApplication.CreateSlimBuilder` + `EnableRequestDelegateGenerator=true`，全程零反射，可 `dotnet publish -p:PublishAot=true` 产出单文件可执行（win-x64 ~11.5MB），AOT 警告数为 0。
  - 多租户：进程内 `TsdbRegistry`（`ConcurrentDictionary<string, Tsdb>`）+ `DataRoot/<db>/` 子目录隔离，启动时按需加载已存在数据库；`POST /v1/db`（admin）创建、`DELETE /v1/db/{db}`（admin）销毁、`GET /v1/db` 列表；数据库名校验通过 `[GeneratedRegex]` 源生成器。
  - SQL 端点：`POST /v1/db/{db}/sql`（单条）+ `POST /v1/db/{db}/sql/batch`（多条），结果以 `application/x-ndjson` 流式返回（meta 行 + 每行 JSON 数组 + end 行），通过手写 `Utf8JsonWriter` 避免多态序列化；其余 DTO 全部走 `System.Text.Json` 源生成器（`ServerJsonContext`）。
  - 认证：`Authorization: Bearer <token>` 三角色（`admin` / `readwrite` / `readonly`），自定义中间件直接读 `ServerOptions.Tokens` 静态映射，非 `/healthz` `/metrics` 一律强制鉴权；写操作（INSERT/DELETE/DDL）需 `readwrite` 或 `admin`，建删数据库需 `admin`。
  - 可观测性：`GET /healthz` 返回 JSON 健康摘要；`GET /metrics` 输出 Prometheus 文本格式（`sonnetdb_uptime_seconds` / `sonnetdb_databases` / `sonnetdb_sql_requests_total` / `sonnetdb_sql_errors_total` / `sonnetdb_rows_inserted_total` / `sonnetdb_rows_returned_total` / per-db `sonnetdb_segments{db="..."}`）。
  - 6 个端到端集成测试（`tests/SonnetDB.Tests/ServerEndToEndTests.cs`）覆盖 Healthz / Metrics 匿名访问、SQL 鉴权、admin 角色限定、CREATE→INSERT→SELECT→DROP 全链路、ndjson 解析、未知数据库 404。
- **整库 Native AOT 兼容**：`Directory.Build.props` 默认开启 `IsAotCompatible=true`（测试与基准项目显式关闭）；`SonnetDB` / `SonnetDB.Cli` / `SonnetDB` 全部以零 IL/AOT 警告通过 `dotnet publish -p:PublishAot=true`。
  - `SndbDataReader.GetFieldType` 重构：内部 `Type[]` 改为 `enum ColumnTypeKind`，并添加 `[DynamicallyAccessedMembers]` 标注 + `typeof(...)` 常量 switch，消除 IL2063/IL2093 警告，对外 API 与运行时行为完全保持。
- **CI**：`.github/workflows/ci.yml` 新增 `aot-publish` job（Linux + Windows 矩阵），执行 `dotnet publish -p:PublishAot=true /warnaserror` 验证 `SonnetDB.Cli` 与 `SonnetDB`，并上传 publish 产物（PR #32）。

### Changed
- `InsertBenchmark`、`QueryBenchmark`、`AggregateBenchmark`：将内存占位实现替换为真实 `Tsdb` 引擎调用（PR #35）
- `README.md` 性能基准章节扩展为 **SonnetDB vs SQLite vs InfluxDB 2.7 vs TDengine 3.3.4** 四方对比（基于 1M 点数据集，单机容器）

### Fixed
- 基准测试 `GlobalCleanup` 中 SQLite 连接池文件锁问题（`SqliteConnection.ClearAllPools()`）（PR #35）
- `_influxAvailable` 现正确使用 `PingAsync()` 返回值而非无条件设为 `true`（PR #35）
- `InsertBenchmark.GlobalCleanup` 不再删除 InfluxDB bucket，避免后续 benchmark 进程的 `IterationSetup` 因 bucket 缺失而抛 `NotFoundException`
- `EnsureInfluxBucketAsync()`：三个 benchmark 在 `GlobalSetup` 中自动创建缺失的 `benchmarks` bucket
- TDengine SQL：`value` / `host` 列名加反引号绕开保留字解析错误，确保 4-DB 全部产出有效结果


### Added
- 新增段文件编码 / 字节统计快照 `SegmentReader.GetStats()`（PR #31）
  - 新增公开 record `SonnetDB.Storage.Segments.SegmentStats`（含 `BlockCount` / `TotalPointCount` / `TotalFieldNameBytes` / `TotalTimestampPayloadBytes` / `TotalValuePayloadBytes` / `RawTimestampBlocks` / `DeltaTimestampBlocks` / `RawValueBlocks` / `DeltaValueBlocks` / `ByFieldType` 以及计算型属性 `AverageTimestampBytesPerPoint` / `AverageValueBytesPerPoint`）与 `FieldTypeStats`（`BlockCount` / `PointCount` / `ValuePayloadBytes` / `DeltaValueBlocks`），为运维巡检、压缩率对比、基准测试提供结构化输出。
  - `SegmentReader.GetStats()`：按需遍历 `BlockDescriptor[]`，一次迭代同时计算总量 / 按 `BlockEncoding` 拆分 / 按 `FieldType` 分组三个维度；不缓存。可用于对同一 `MemTable` 分别以 V1 / V2 写入后对比 `Total*PayloadBytes` 验证压缩效果。
  - `SegmentStats.ByFieldType` 使用 `IReadOnlyDictionary<FieldType, FieldTypeStats>` 提供面向查询，默认为空字典以避免空段访问 NRE。
  - 6 个新测试（`SegmentReaderStatsTests`）覆盖：默认 V1 全部计入 raw 且字节数符合 8B/点；单独开启 V2 时间戳验证只选取时间戳压缩、值字节数不变；单独开启 V2 值（String 字典）验证值字节压缩、时间戳保持 V1；双 V2两个计数器都增加、平均字节/点均 < 8；多 `FieldType` 混合段按组计数与点数一致；空 `SegmentStats` 除零防护。

- 新增数值列 V2 编码：Float64 Gorilla XOR + Boolean RLE + String 字典（PR #30）
  - 新增内部位流工具 `SonnetDB.Storage.Segments.BitIo`：`BitWriter` / `BitReader` ref struct，高位优先按位写读，最大 64 位/调用。
  - 新增内部值列 V2 编解码器 `SonnetDB.Storage.Segments.ValuePayloadCodecV2`：
    - **Float64**：简化版 Gorilla XOR — 第一个值 64 位锚点，之后每点 1 位控制位；变化点再写 6 位 leadingZeros + 6 位 (meaningful-1) + meaningful 位有效位。常量序列压缩到 ≈1 位/点。
    - **Boolean**：游程长度编码（RLE）— 1 字节初值 + 交替 varint 段长。
    - **String**：按出现顺序构建字典 — `varint(dictSize)` + `dictSize × (varint(byteLen) + UTF-8)` + `count × varint(idx)`，重复值高度压缩。
    - **Int64**：本 PR 暂不压缩，仍为 8B LE 直存（与 V1 等价）。
  - `SegmentWriterOptions.ValueEncoding`：默认 `None`（V1）以保证已有段文件与测试行为不变；显式设为 `DeltaValue` 启用 V2 并在 `BlockHeader.Encoding` 与 `TimestampEncoding` 标志位独立组合。
  - `BlockDecoder.ReadValues` / `ReadValuesRange` 新增基于 `descriptor.ValueEncoding` 的 V1/V2 分发；V2 范围读取需先全量解码再切片（XOR/RLE/字典本质顺序）。
  - 19 个新测试（`ValuePayloadCodecV2Tests`）覆盖：Float64 空/单点/常量序列压缩/递增序列/特殊值（NaN/±Inf/±0）round-trip；Bool 全 true/交替/混合 run/损坏 run 越界；String 全相同/含 unicode/含空串/字典索引越界；Int64 V2 透传；SegmentWriter 默认无标志、单独 `DeltaValue`（Float64/Bool/String 均显著小于 V1）、`DeltaTimestamp | DeltaValue` 双标志组合及 `DecodeBlockRange` 与 V1 一致。

- 新增时间戳 Delta-of-Delta + ZigZag varint 编码（V2 block payload，向后兼容 V1）（PR #29）
  - 新增内部 `SonnetDB.Storage.Segments.TimestampCodec`：`MeasureDeltaOfDelta` / `WriteDeltaOfDelta` / `ReadDeltaOfDelta`。V2 格式：8 字节定点锐 + 1 个一阶差分 + 剩余二阶差分，常规采样间隔下压缩到 ≈1 字节/点。
  - `BlockEncoding` 改为 `[Flags]`：`DeltaTimestamp` (1) 与 `DeltaValue` (2) 可独立开关；`SegmentReader` 根据 bit 拆分到 `BlockDescriptor.{TimestampEncoding, ValueEncoding}`。
  - `SegmentWriterOptions.TimestampEncoding`：默认 `None`（V1）以保证已有文件与测试行为不变；显式设为 `DeltaTimestamp` 则启用 V2 并在 `BlockHeader.Encoding` 中置位。
  - `BlockDecoder` 联合读取路径（全量与范围）根据 `descriptor.TimestampEncoding` 分发；V2 路径需要完整重现时间戳后才能二分，已与现有范围查询逻辑保持一致。
  - 13 个新测试（`TimestampCodecTests`）覆盖：空序列、单点、规则间隔压缩占比、不规则间隔、负二阶差分、大锐点、buffer 长度不匹配、锐点截断、varint 越界、SegmentWriter 默认 V1、V1↔V2 跳点一致、`DecodeRange` 一致、`BlockDescriptor` 标志保留。

- 新增标准 ADO.NET API，提供 `SndbConnection` / `SndbCommand` / `SndbDataReader` / `SndbParameter` / `SndbParameterCollection` / `SndbConnectionStringBuilder`（PR #28）
  - `SonnetDB.Ado.SndbConnection : System.Data.Common.DbConnection`：连接字符串为 `Data Source=<根目录>`（大小写不敏感，由 `DbConnectionStringBuilder` 提供）；同进程同路径多次 `Open` 通过内部 `SharedSndbRegistry` 引用计数共享同一 `Tsdb`，避免 WAL 锁冲突；事务与 `ChangeDatabase` 抛 `NotSupportedException`
  - `SonnetDB.Ado.SndbCommand : DbCommand`：包装 `SqlExecutor`；`ExecuteNonQuery` 返回 INSERT 写入行数 / DELETE 増加的墓碑总数 / CREATE MEASUREMENT 0 / SELECT -1；`ExecuteScalar` 返回 SELECT 首行首列（空集返 null）；`ExecuteReader` 包装 `SelectExecutionResult`，非 SELECT 语句返回零行 reader 并携带 `RecordsAffected`
  - 参数绑定：支持 `@name` 与 `:name` 占位符，执行前以状态机扫描 SQL 文本并跳过字符串字面量 / 双引号标识符 / 行注释；支持类型包括 `string` / `bool` / 整型 / 浮点 / `decimal` / `DateTime` / `DateTimeOffset`（后两者转为 Unix 毫秒）/ `null` / `DBNull`；字符串值会被单引号包裹并把内部 `'` 转义为 `''`，避免 SQL 注入
  - `SndbDataReader : DbDataReader`：完整实现 `Read` / `GetXxx` / `IsDBNull` / `GetOrdinal` / `GetFieldType`（以首个非 null 行推断）/ `HasRows` / `RecordsAffected` / `CommandBehavior.CloseConnection`。`NextResult` 总为 `false`，`GetBytes` / `GetChars` 抛 `NotSupported`
  - 单元测试：31 个端到端测试（`TsdbAdoApiTests`）覆盖连接生命周期 / 共享 `Tsdb` / `BeginTransaction` 不支持 / `ConnectionStringBuilder` 大小写不敏感 / 三种 `ExecuteXxx` / 参数状态机（跳过字面量与标识符）/ 参数转义防注入（`O'Brien` 场景）/ 缺失参数报错 / `:name` 形式 / NULL 参数 / 多个 CommandText 错误路径 / `CloseConnection` 行为

- 新增 Tag 倒排索引以加速 `SELECT/DELETE` 的 `WHERE tag = '...'` 过滤（PR #27）
  - `SonnetDB.Catalog.TagInvertedIndex`（internal）：维护 `measurement → SeriesId 集合` 与 `measurement → tagKey → tagValue → SeriesId 集合` 两级映射；全部使用 `ConcurrentDictionary` 实现单写多读线程安全；集合本身用 `ConcurrentDictionary<ulong, byte>` 模拟并发集合
  - `SonnetDB.Catalog.SeriesCatalog.Find(measurement, tagFilter)`：从全表线性扫描改为基于倒排索引的候选集交集（基准选最小集合，规模上界为 `min(|S_i|)`）；返回前仍执行一次防御性 measurement+tag 重校验以容忍倒排索引与 `_byCanonical` 的瞬间不一致
  - 索引在 `GetOrAddInternal` 中仅由胜出的 `candidate` 线程写入（`ReferenceEquals(entry, candidate)`），并在 `LoadEntry`（`CatalogFileCodec` 重放路径）与 `Clear` 中维护——索引本身不进入持久化格式，启动时由现有持久化条目重建，因此**未变更磁盘 catalog 文件格式**
  - 单元测试：11 个新增测试（`TagInvertedIndexTests`）覆盖无 tag 过滤 / 单 tag / 多 tag 交集 / 未命中值 / 缺失 tagKey / 未知 measurement / measurement 隔离 / `Clear` 后清空 / 重复 `GetOrAdd` 索引不膨胀 / `LoadEntry` 重建 / 并发写读

- 新增 SQL `DELETE FROM ... WHERE ...` 执行支持（PR #26）
  - `SonnetDB.Sql.Execution.DeleteExecutionResult`（record，含 `Measurement` / `SeriesAffected` / `TombstonesAdded`）
  - `SonnetDB.Sql.Execution.DeleteExecutor`（internal）：复用 `WhereClauseDecomposer` 解析 tag 等值过滤 + 时间窗，对所有命中 tag 过滤的 series × schema 中所有 Field 列调用 `Tsdb.Delete(seriesId, fieldName, from, to)`，落到 PR #20 的 Tombstone 体系（WAL 追加 + 内存墓碑表 + 查询时过滤）
  - `SqlExecutor.ExecuteDelete(Tsdb, DeleteStatement)` 公共入口；`Execute` 派发新增 `DeleteStatement` 分支
  - 语义：`WHERE host = 'h1' AND time >= a AND time <= b` 等价于命中 series 的所有 field 列在 `[a, b]` 闭区间打墓碑；省略 time 比较则覆盖全时间轴；省略 tag 过滤则作用于该 measurement 下所有 series；命中 0 series 直接返回零计数（不抛错）
  - 校验规则：measurement 必须存在；WHERE 与 SELECT 共用同一套约束（仅 AND、tag 等值、time 比较、不支持 OR/NOT/field 过滤）；空时间窗抛 `InvalidOperationException`
  - 单元测试：13 个端到端测试覆盖时间窗 + tag 过滤 / 仅时间窗 / 仅 tag 过滤 / `time = X` 单点删除 / 命中 0 series / 跨重启持久化（WAL replay）/ 删除后聚合验证 / 各类错误场景（缺 measurement / OR / field 过滤 / 未知 tag 列 / 空时间窗 / null 参数）

- 新增 SQL `SELECT ... [WHERE ...] [GROUP BY time(...)]` 执行支持（PR #25）
  - `SonnetDB.Sql.Execution.SelectExecutionResult`（record，含 `Columns` / `Rows`；行内运行时类型：time→`long`、tag→`string?`、field→`double/long/bool/string?`、count→`long`、其他聚合→`double`）
  - `SonnetDB.Sql.Execution.WhereClauseDecomposer`（internal）：将 WHERE AST 拆分为 `(TagFilter, TimeRange)`；仅支持顶层 `AND` 合取、`tag = 'literal'` 等值过滤、`time {= != >= > <= <}` 时间窗（`time !=` 暂不支持）；OR / NOT / field 过滤 / 非字面量右值 / 同 tag 列冲突值均抛 `InvalidOperationException`；自动检测空时间窗
  - `SonnetDB.Sql.Execution.SelectExecutor`（internal）：投影分类（time/tag/field/aggregate）；原始模式按 series 做时间戳并集 outer-join，缺失字段输出 `null`；聚合模式以 `SortedDictionary<long, BucketState[]>` 按桶累积 count/sum/min/max/first/last，`GROUP BY time(d)` 由 `TimeBucket.Floor` 对齐，无 GROUP BY 则全局单桶；多 series 的 sum/avg/min/max/count 自动跨 series 合并；`count(*)` 跨 schema 全部数值 field 求总点数（跳过 String）；`count(field)` 计数任意类型；其他聚合拒绝 String field
  - `SqlExecutor.ExecuteSelect(Tsdb, SelectStatement)` 公共入口；`Execute` 派发新增 `SelectStatement` 分支
  - 校验规则：聚合不可与裸列混用；`GROUP BY time(...)` 仅在聚合中有效；`first`/`last` 多 series 暂不支持（v1）；未知函数 / 未知列 / 聚合函数作用于 Tag 列均抛错
  - 单元测试：25 个端到端测试覆盖 `SELECT *` / 列投影 / outer-join NULL / WHERE 时间窗 / WHERE tag 过滤 / 别名 / `count(*)` / `count(field)` / `sum/avg/min/max` / `first/last` / 多 series 聚合 / `GROUP BY time(1000ms)` / 空时间窗 / 各类错误场景（缺 measurement / 未知列 / OR / field 过滤 / 混合投影 / 缺聚合的 GROUP BY / first 多 series / tag 不等 / tag 冲突 / String 字段 sum）

- 新增 SQL `INSERT INTO ... VALUES (...)` 执行支持（PR #24）
  - `SonnetDB.Sql.Execution.InsertExecutionResult`（record，含 `Measurement` / `RowsInserted`）
  - `SqlExecutor.ExecuteInsert(Tsdb, InsertStatement)`：完整列绑定 + 类型校验 + 时间戳缺省 + 批量写入
  - 校验规则：measurement 必须已 CREATE；列名必须存在于 schema；同一 INSERT 列列表禁止重复；Tag 必须为字符串字面量且非 NULL；Field 类型必须匹配（INT 字面量可隐式提升为 FLOAT）；每行至少 1 个 Field 列值；`time` 列必须为非负整数字面量；缺省时使用 `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()`
  - `SqlParser`：新增内部 `ExpectColumnName()`，允许 INSERT 列列表中将保留字 `time` 作为列名使用（与时间戳伪列对应；亦可继续用 `"time"` 引号转义）
  - `SqlExecutor.ExecuteStatement` 现支持 `InsertStatement` 派发
  - 单元测试：17 个端到端测试，覆盖单行 / 批量 / 时间缺省 / 时间大小写不敏感 / Int→Float 提升 / 全四种 FieldType round-trip / 仅 Field 无 Tag / measurement 缺失 / 未知列 / 重复列 / 类型不匹配 / Tag 非字符串 / NULL / 缺 Field / 负时间戳 / 批量部分失败前序已落地 / 参数 null

- 新增 `SonnetDB.Catalog` 命名空间下 measurement schema 体系，并接入 `Tsdb` 与 SQL 执行器（PR #23）
  - `MeasurementColumnRole`（Tag/Field 角色枚举）、`MeasurementColumn`（列定义 record）、`MeasurementSchema`（不可变值对象，工厂 `Create` 校验：列非空、≥1 个 Field、列名唯一、Tag 列必须 STRING、禁止 Unknown 类型）、`MeasurementCatalog`（基于 `ConcurrentDictionary` 的线程安全注册表）
  - `MeasurementSchemaCodec`：新增持久化文件 `measurements.tslschema`；二进制格式 `Magic(8) + FormatVersion(4) + HeaderSize(4) + Count(4) + Reserved(12)` 头 + 变长 measurement 记录 + `Crc32(4) + Magic(8) + Reserved(4)` 尾；`ArrayPool<byte>` + `SpanReader` / `SpanWriter` 实现，`Save` 走临时文件 + 原子 rename + fsync
  - `TsdbPaths.MeasurementSchemaFileName` / `MeasurementSchemaPath(root)` 路径常量
  - `Tsdb.Measurements` 属性 + `Tsdb.CreateMeasurement(MeasurementSchema)`：注册到 catalog 并立刻把全量 schema 集合原子持久化（崩溃安全）；`Open` 启动时加载、`Dispose` 关闭时再次保存
  - 新增 `SonnetDB.Sql.Execution.SqlExecutor`：`Execute(Tsdb, sql)` / `ExecuteStatement(Tsdb, SqlStatement)` / `ExecuteCreateMeasurement(Tsdb, CreateMeasurementStatement)`；把 AST `ColumnDefinition` 映射到 catalog `MeasurementColumn` 后调用 `Tsdb.CreateMeasurement`；其余语句类型暂抛 `NotSupportedException` 留待后续 PR
  - 单元测试：8 个 schema 校验 + 5 个 codec round-trip / 损坏检测 + 5 个执行器与持久化端到端测试

- 新增 `SonnetDB.Sql` 命名空间：纯 Safe-only、零第三方依赖的 SQL 词法 + 语法分析器（PR #22）
  - `TokenKind` / `Token` / `SqlLexer`：单遍词法分析；关键字大小写不敏感；标识符保留原始大小写；支持单引号字符串字面量（`''` 转义）、双引号引用标识符（`""` 转义）、整数/浮点字面量、duration 字面量（`ns / us / ms / s / m / h / d`，统一归一化为毫秒）、`-- 行注释`、`/* 块注释 */`、运算符 `= != <> < <= > >= + - * / %`
  - `Sql.Ast`：AST 节点（`SqlStatement` / `CreateMeasurementStatement` / `InsertStatement` / `SelectStatement` / `DeleteStatement` / `ColumnDefinition` / `SelectItem` / `TimeBucketSpec` / `SqlExpression` 派生：`LiteralExpression` / `DurationLiteralExpression` / `IdentifierExpression` / `StarExpression` / `FunctionCallExpression` / `BinaryExpression` / `UnaryExpression`），均为 `record` 值语义
  - `SqlParser`：递归下降解析器，覆盖 `CREATE MEASUREMENT` / `INSERT INTO ... VALUES (...) [, (...)]*` / `SELECT projections FROM measurement [WHERE ...] [GROUP BY time(duration)]` / `DELETE FROM measurement WHERE ...`；支持 `*` 通配、聚合函数（`count(*) / avg(x) / ...`）、`AS alias` 与裸 alias、`AND / OR / NOT` 短路逻辑、6 种比较与 5 种算术运算、括号显式优先级、`NULL / TRUE / FALSE` 字面量；新增 `SqlParser.Parse(string)` 解析单语句、`SqlParser.ParseScript(string)` 解析多语句脚本（分号分隔）
  - 关键字 `time` 在表达式中既可作为列名（`time >= 100`）也可作为函数（`time(1m)`），通过下一个 token 是否为 `(` 自动消歧
  - `SqlParseException`：携带源 SQL 字符位置的诊断异常
  - 单元测试：50 个 Lexer + Parser 测试，覆盖关键字大小写、字符串/标识符/duration 转义、运算符优先级、注释跳过、错误位置等

- 新增 `SonnetDB.Engine.Retention.RetentionPolicy`：数据保留策略；支持全局 TTL、轮询周期、限流（MaxTombstonesPerRound）及虚拟时钟注入（NowFn）（PR #21）
- 新增 `SonnetDB.Engine.Retention.RetentionPlan` / `TombstoneToInject`：单次 Retention 扫描的产物（纯计算，无副作用）
- 新增 `SonnetDB.Engine.Retention.RetentionPlanner`：从当前段集合产出 `RetentionPlan` 的纯函数；支持整段 drop、部分过期墓碑注入、已有等价墓碑去重及限流截断
- 新增 `SonnetDB.Engine.Retention.RetentionWorker`：后台 Retention 工作线程，双路径回收——整段直接 drop（MaxTimestamp < cutoff） + 墓碑注入（部分过期段，由 Compaction 在下一轮物理删除）
- 新增 `SonnetDB.Engine.Retention.RetentionExecutionStats`：单次 Retention 扫描统计（Cutoff / DroppedSegments / InjectedTombstones / ElapsedMicros）
- `SegmentManager.DropSegments(IReadOnlyList<long>)`：原子移除多个段，重建索引快照，Dispose 旧 reader，返回被移除列表（PR #21）
- `Tsdb.Retention`：暴露后台 Retention 工作线程（仅当 `TsdbOptions.Retention.Enabled=true` 时非 null）
- `TsdbOptions.Retention`：Retention TTL 策略入口（默认禁用，保持向后兼容）
- `RetentionPolicy.NowFn` 支持注入虚拟时钟（测试 + 自定义时间戳单位）

**Milestone 5 完成**（PR #17 后台 Flush + #18 Compaction + #19 多 WAL 滚动 + #20 DELETE-Tombstone + #21 Retention TTL）。

- 删除支持：`Tsdb.Delete(seriesId, field, from, to)` 和 `Tsdb.Delete(measurement, tags, field, from, to)`，返回操作是否成功（PR #20）
- WAL 新增 `RecordType.Delete = 5`（向后兼容），`WalWriter.AppendDelete` / `WalSegmentSet.AppendDelete` 追加删除记录
- 新增 `Tombstone`（readonly record struct）：墓碑数据结构，声明 (SeriesId, FieldName) 在时间窗 [From, To] 内的数据已被永久标记删除（v1 时间窗语义，无 perPoint LSN 比对）
- 新增 `TombstoneTable`：进程内墓碑集合，按 (SeriesId, FieldName) 索引；线程安全（lock 写 + Volatile 读快照）；提供 `IsCovered` / `GetForSeriesField` / `Add` / `LoadFrom` / `RemoveAll`
- 新增 `TombstoneManifestCodec`（`SonnetDB.Wal`）：墓碑清单文件 `<root>/tombstones.tslmanifest` 的序列化与反序列化；包含 Magic / FormatVersion / Crc32 校验；临时文件 + 原子 rename 写入
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


- 新增 `SonnetDB.Wal.WalSegmentSet`：多 WAL segment 管理器（Append / Sync / Roll / RecycleUpTo / ReplayWithCheckpoint），支持多 segment 滚动写入与按 CheckpointLsn 整段回收（PR #19）
- 新增 `WalSegmentLayout`（static）：WAL segment 文件命名约定（`{startLsn:X16}.SDBWAL`）、枚举、`TryParseStartLsn` 及 legacy `active.SDBWAL` 升级工具
- 新增 `WalSegmentInfo`（readonly record struct）：segment 元数据（StartLsn / Path / FileLength）
- 新增 `WalRollingPolicy`：WAL 滚动策略配置（Enabled / MaxBytesPerSegment=64MB / MaxRecordsPerSegment=1M 双阈值）
- 新增 `Tsdb` 启动时的 legacy `wal/active.SDBWAL` 自动升级路径（`UpgradeLegacyIfPresent`）

### Changed
- `FlushCoordinator.Flush` 改为通过 `WalSegmentSet` 工作；Flush 顺序升级为：WriteSegment → AppendCheckpoint+Sync → Roll → RecycleUpTo(checkpointRecordLsn) → MemTable.Reset（PR #19）
- `Tsdb` 内部 `_walWriter` 替换为 `WalSegmentSet _walSet`；`Tsdb.Open` 现在调用 `WalSegmentSet.Open`（自动升级 legacy WAL）和 `WalSegmentSet.ReplayWithCheckpoint`
- `TsdbOptions` 新增 `WalRollingPolicy WalRolling` 属性（默认 `WalRollingPolicy.Default`）
- `WalTruncator.SwapAndTruncate` 标记 `[Obsolete]`，内部保留以兼容外部使用；替代方案：`WalSegmentSet.Roll + RecycleUpTo`


- 新增 `SonnetDB.Engine.Compaction.CompactionPolicy`：Size-Tiered Compaction 触发策略（Enabled / MinTierSize / TierSizeRatio / FirstTierMaxBytes / PollInterval / ShutdownTimeout）
- 新增 `CompactionPlan` / `CompactionResult`：Compaction 计划与执行结果数据对象
- 新增 `CompactionPlanner`（static）：无副作用的 Size-Tiered 计划生成器；tier 划分公式 `tierIndex = max(0, floor(log_TierSizeRatio(fileLength / FirstTierMaxBytes)) + 1)`
- 新增 `SegmentCompactor`：N 路最小堆合并多个段、按 (SeriesId, FieldName) 写入新段；v1 同 timestamp 全部保留、FieldType 冲突抛 `InvalidOperationException`
- 新增 `CompactionWorker`（internal）：后台 Compaction 工作线程，轮询 Plan + Execute + SwapSegments + 删除旧段
- 新增 `SegmentManager.SwapSegments`：在单一锁内原子地移除旧段 + 打开新段 + 重建索引快照，避免中间状态可见
- `TsdbOptions.Compaction` 新增 `CompactionPolicy` 属性（默认 Default，Enabled=true）
- `Tsdb.Open` 末尾：若 `Compaction.Enabled` 启动 `CompactionWorker`
- `Tsdb.Dispose`：先关 CompactionWorker，再关 FlushWorker
- `Tsdb.AllocateSegmentId()`（internal）：线程安全 SegmentId 分配


- 新增 `SonnetDB.Engine.BackgroundFlushWorker`（internal）：后台 Flush 工作线程，含信号 + 周期轮询双触发，与同步 FlushNow 共享 `_writeSync` 锁保证互斥
- 新增 `BackgroundFlushOptions`（Enabled / PollInterval / ShutdownTimeout），`Dispose` 严格不泄漏后台线程
- 新增 `WalReplay.ReplayIntoWithCheckpoint`：基于 Checkpoint LSN 两遍扫描跳过冗余 WritePoint，消除崩溃恢复的冗余回放开销
- 新增 `WalReplayResult` record（CheckpointLsn / LastLsn / WritePoints）
- `TsdbOptions.BackgroundFlush` 暴露后台线程开关（默认 Enabled=true）
- `Tsdb.CheckpointLsn` 诊断属性：最近一次 Flush 的 WAL CheckpointLsn
- `Tsdb.Write` 在锁外向 worker 发送非阻塞信号；移除同步 Write 路径中的自动 Flush（由后台线程接管）
- `Tsdb.Open` 改用 `ReplayIntoWithCheckpoint` 替代 `ReplayInto`，支持 WAL 续写正确 LSN

### Added
- 新增 `SonnetDB.Query.QueryEngine`：合并 MemTable + 多 Segment 的查询执行器；支持原始点查询（`Execute(PointQuery)`）、聚合查询（`Execute(AggregateQuery)`）及批量聚合（`ExecuteMany`）（Milestone 4 完成）
- 新增 `PointQuery` / `AggregateQuery` / `AggregateBucket` / `Aggregator` / `TimeRange` 查询类型
- 支持 Count / Sum / Min / Max / Avg / First / Last 七种聚合函数（Float64 / Int64 / Boolean 字段）
- 支持 `GROUP BY time(...)` 桶聚合（基于 PR #7 的 `TimeBucket`）；空桶不输出
- 内部 N 路有序合并器 `BlockSourceMerger`：段按 SegmentId 升序排列后合并，MemTable 在最末，同 ts 全部 yield（不去重）
- `Tsdb.Query` 属性暴露查询入口（`QueryEngine` 无状态，每次查询时重建 SegmentId→Reader 映射）
- **Milestone 4 完成**：查询路径全面贯通（MemTable + 多段 + 时间过滤 + 7 种聚合 + GROUP BY time）

### Added
- 新增 `SonnetDB.Storage.Segments.SegmentBlockRef`（readonly struct）：跨段统一的 Block 引用（SegmentId + SegmentPath + BlockDescriptor）
- 新增 `SegmentIndex`（sealed class）：单段内 SeriesId / (SeriesId, FieldName) → BlockDescriptor 索引，含段级时间范围与时间窗二分剪枝
- 新增 `MultiSegmentIndex`（sealed class）：跨段只读联合索引快照；`LookupCandidates` 剪枝顺序：段级时间 → series → field → 段内时间窗二分
- 新增 `SonnetDB.Engine.SegmentManager`（sealed class）：已打开 SegmentReader 集合 + 索引快照管理器；lock 写 + Volatile 无锁读并发模型
- `Tsdb` 接入 `SegmentManager`：Open 时扫描段构建初始索引，FlushNow 时增量 AddSegment；Dispose 时关闭全部 SegmentReader
- `TsdbOptions` 新增 `SegmentReaderOptions` 属性

### Added
- 启动 Milestone 4：查询路径
- 新增 `SonnetDB.Storage.Segments.SegmentReader`：不可变段文件只读访问器
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
- 启动 SonnetDB 引擎门面：`SonnetDB.Engine.Tsdb`（Open / Write / WriteMany / FlushNow / Dispose），完成 Milestone 3 写入路径闭环（PR #13）
- 新增 `TsdbOptions`：引擎全局配置（RootDirectory / FlushPolicy / SegmentWriterOptions / WalBufferSize / SyncWalOnEveryWrite）
- 新增 `TsdbPaths`：标准磁盘布局路径管理（catalog.SDBCAT + wal/active.SDBWAL + segments/{id:X16}.SDBSEG）
- 新增 `FlushCoordinator`：MemTable → Segment + WAL Checkpoint + WAL Truncate 三步原子可见
- 新增 `WalTruncator.SwapAndTruncate`：rename + 重建策略，避免就地截断的并发风险
- 新增 `SegmentWriterOptions.PostRenameAction`（internal）：原子 rename 完成后的测试钩子，用于模拟 rename 之后崩溃场景
- 完成 Milestone 3：写入路径闭环，三场景崩溃恢复测试矩阵齐全（未 Flush 崩溃 / Flush 后崩溃 / rename 后未 Checkpoint 崩溃）

### Added
- 初始化项目规划文档：`README.md`、`CHANGELOG.md`、`ROADMAP.md`、`AGENTS.md`
- 确定技术栈：C# / .NET 10 / xUnit / BenchmarkDotNet / GitHub Actions
- 确定核心设计原则：Safe-only、Span/MemoryMarshal、InlineArray、WAL+MemTable+Segment
- 解决方案与项目骨架（`SonnetDB.slnx`、`src/SonnetDB`、`src/SonnetDB.Cli`、`tests/SonnetDB.Core.Tests`、`tests/SonnetDB.Benchmarks`）（PR #2）
- `Directory.Build.props`（统一 `LangVersion` / `Nullable` / `ImplicitUsings` / `TreatWarningsAsErrors`）
- `Directory.Packages.props`（Central Package Management）
- `global.json`（固定 .NET 10 SDK）
- `.editorconfig`（统一代码风格）
- 新增 GitHub Actions CI 工作流（build + test，ubuntu / windows 矩阵）
- 新增 CodeQL 安全扫描工作流
- 新增 Dependabot 依赖更新配置
- 新增 dotnet format 校验
- 新增时序数据库性能对比基准（`tests/SonnetDB.Benchmarks/`）：使用 BenchmarkDotNet 0.15.8 对比 SonnetDB（内存占位）、SQLite、InfluxDB 2.x 和 TDengine 3.x 在相同 Docker 环境下的 100 万条数据**写入、时间范围查询、1 分钟桶聚合**的性能，含 Docker Compose 配置和 README 说明
- 新增 `SonnetDB.IO.SpanWriter`：基于 Span/MemoryMarshal/BinaryPrimitives 的 safe-only 顺序二进制写入器
- 新增 `SonnetDB.IO.SpanReader`：基于 Span/MemoryMarshal/BinaryPrimitives 的 safe-only 顺序二进制读取器
- 支持基础类型、unmanaged 结构体、结构体数组、VarInt(LEB128)、字符串的 round-trip 编解码
- 全程 little-endian，零 `unsafe`（PR #4）
- 新增 `SonnetDB.Buffers.InlineBytes4/8/16/32/64`：基于 `[InlineArray(N)]` 的固定长度内联缓冲区
- 新增 `InlineBytesExtensions`：通过 `MemoryMarshal.CreateSpan` 提供 Safe-only 的 `AsSpan` / `AsReadOnlySpan` 视图
- 新增 `InlineBytesHelpers`：泛型 `SequenceEqual` / `CopyFrom` 辅助方法
- 新增 `TsdbMagic`：定义 SonnetDB 文件 / 段 / WAL 的 magic 与格式版本常量（PR #5）
- 新增固定二进制结构体（namespace `SonnetDB.Storage.Format`）：
  - `FileHeader`（64B）/ `SegmentHeader`（64B）/ `BlockHeader`（64B）
  - `BlockIndexEntry`（48B）/ `SegmentFooter`（64B）/ `WalRecordHeader`（32B）
- 新增枚举：`BlockEncoding` / `FieldType` / `WalRecordType`
- 新增 `FormatSizes` 常量类，所有 header 尺寸由编译期 `Unsafe.SizeOf<T>` 测试守护
- 完成 Milestone 1：内存与二进制基础设施（Span/MemoryMarshal/InlineArray + 全部固定 header）（PR #6）
- 新增逻辑数据模型（namespace `SonnetDB.Model`）：
  - `FieldValue`（readonly struct，零装箱，支持 Float64/Int64/Boolean/String）
  - `Point`（用户层写入对象，含校验规则）
  - `DataPoint`（引擎内单 field 数据点，readonly record struct）
  - `SeriesFieldKey`（series + field 复合键，readonly record struct）
  - `AggregateResult`（Count/Sum/Min/Max/Avg 累加器）
  - `TimeBucket`（时间桶 Floor/Range/Enumerate 辅助）
- 启动 Milestone 2：逻辑模型与 Series Catalog（PR #7）
- 新增 `SonnetDB.Model.SeriesKey`（readonly struct）：规范化 `measurement + sorted(tags)` 为确定性字符串，格式 `measurement,k1=v1,k2=v2`，tags 按 key Ordinal 升序
- 新增 `SonnetDB.Model.SeriesId`（static class）：通过 `XxHash64` 将 `SeriesKey.Canonical` 的 UTF-8 编码折叠为 `ulong`，作为引擎主键（PR #8）
- 新增 `SonnetDB.Catalog.SeriesCatalog`：线程安全的 SeriesKey ↔ SeriesId ↔ SeriesEntry 中央目录（基于 ConcurrentDictionary，单写多读友好）
- 新增 `SonnetDB.Catalog.SeriesEntry`：序列目录条目（Id / Key / Measurement / Tags / CreatedAtUtcTicks），Tags 以 FrozenDictionary 保证不可变
- 新增 `SeriesCatalog.Find`：按 measurement + tag 子集线性查找
- 新增 `SonnetDB.Catalog.CatalogFileCodec`：`.SDBCAT` 目录文件序列化器（含临时文件原子替换写入与规范化校验加载）
- 新增 `SonnetDB.Storage.Format.CatalogFileHeader`（64B）：目录文件头，含 magic "SDBCATv1" / 版本 / 条目数
- 新增 `TsdbMagic.Catalog`（"SDBCATv1"）与 `TsdbMagic.CreateCatalogMagic()`
- 新增 `FormatSizes.CatalogFileHeaderSize = 64`
- 新增 `InlineBytes24` 内联缓冲区及其 `AsSpan`/`AsReadOnlySpan` 扩展
- 完成 Milestone 2：逻辑模型与 Series Catalog（PR #9）
- 启动 Milestone 3：写入路径（PR #10）
- 新增 `SonnetDB.Storage.Format.WalFileHeader`（64B）：WAL 文件头，含 magic "SDBWALv1" / 版本 / FirstLsn
- 新增 `FormatSizes.WalFileHeaderSize = 64`
- 更新 `WalRecordHeader`（32B）：新增 `Magic`（0x57414C52）/ `Flags` / `PayloadCrc32` / `Lsn` 字段，移除 `SeriesId` 至 payload
- 更新 `WalRecordType`：重命名 `Write→WritePoint`、`CatalogUpdate→CreateSeries`，新增 `Truncate=4`
- 新增 `SonnetDB.Wal` 命名空间：
  - `WalRecord` 抽象基类及派生：`WritePointRecord` / `CreateSeriesRecord` / `CheckpointRecord` / `TruncateRecord`
  - `WalWriter`：append-only WAL 写入器，含 CRC32（`System.IO.Hashing.Crc32`）+ fsync 支持
  - `WalReader`：迭代式回放，支持文件尾截断与 CRC 校验失败的优雅停止，暴露 `LastValidOffset`
  - `WalReplay`：将 WAL 回放到 `SeriesCatalog`，并 yield 出 `WritePointRecord` 流
  - `WalPayloadCodec`（internal）：4 种 RecordType × 4 种 FieldType 的 payload 编解码
- 新增 `SonnetDB.Memory.MemTableSeries`：单 (SeriesId, FieldName, FieldType) 桶，
  支持顺序与乱序追加，Snapshot 稳定排序（`_isSorted` 快速路径 + 索引辅助稳定排序）
- 新增 `SonnetDB.Memory.MemTable`：以 SeriesFieldKey 为主键的写入内存层，
  支持 WAL Replay 装载（`ReplayFrom`）、阈值触发 Flush（`ShouldFlush`）、Reset 与 SnapshotAll（PR #11）
- 新增 `SonnetDB.Memory.MemTableFlushPolicy`：MaxBytes / MaxPoints / MaxAge 三种阈值策略
- 新增 `SonnetDB.Storage.Segments.SegmentWriter`：把 MemTable 写为不可变 `.SDBSEG` 文件，使用临时文件 + 原子 rename 保证崩溃安全（PR #12）
- 新增 `SegmentWriterOptions`：BufferSize / FsyncOnCommit / TempFileSuffix 写入选项
- 新增 `SegmentBuildResult`：构建结果记录（路径、BlockCount、时间范围、各区偏移、耗时）
- 新增 `ValuePayloadCodec`（internal）：Float64 / Int64 / Boolean / String 的 Raw 编码
- 新增 `FieldNameHash`（internal）：基于 XxHash32 的字段名哈希，用于 BlockIndexEntry.FieldNameHash
- 启用 `BlockHeader.Crc32`（CRC32(FieldNameUtf8 ++ TsPayload ++ ValPayload)）与 `SegmentFooter.Crc32`（IndexCrc32）

---

## [0.1.0] — *Planned*

> 对应 ROADMAP Milestone 0 ～ Milestone 3

### Added
- 解决方案与项目骨架（`SonnetDB.sln`、`src/SonnetDB`、`src/SonnetDB.Cli`、`tests/SonnetDB.Core.Tests`、`tests/SonnetDB.Benchmarks`）
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
- ADO.NET 风格 API：`SndbConnection / SndbCommand / SndbDataReader`

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
- 发布 NuGet 包 `SonnetDB` 0.1.0

---

[Unreleased]: https://github.com/maikebing/SonnetDB/compare/HEAD...HEAD
[0.1.0]: https://github.com/maikebing/SonnetDB/releases/tag/v0.1.0
[0.2.0]: https://github.com/maikebing/SonnetDB/releases/tag/v0.2.0
[0.3.0]: https://github.com/maikebing/SonnetDB/releases/tag/v0.3.0
