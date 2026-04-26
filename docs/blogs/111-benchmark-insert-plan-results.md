## 写入基准通稿：SonnetDB 在嵌入式与服务端两条路径上的吞吐对比

SonnetDB 的写入基准分为两组：进程内嵌入式基准与服务端 HTTP 基准。嵌入式路径关注引擎本体的 WAL、MemTable、Flush 与 Segment 写入效率；服务端路径关注协议解析、认证、HTTP/Kestrel 与批量端点带来的额外开销。

### 通稿

本轮写入基准使用统一的 100 万点 IoT 数据集，在同一台 Windows 11 开发机上对比 SonnetDB Core、SQLite、InfluxDB、TDengine 与 SonnetDB Server。SonnetDB 同时提供进程内嵌入式和服务端部署形态，因此报告会把“引擎本体能力”和“远程服务能力”分开呈现，避免把 HTTP 协议成本误算到核心存储引擎里。

对边缘网关、采集代理、工控盒子等嵌入式场景，核心指标是单进程内批量写入耗时与内存分配；对平台化服务端场景，核心指标是 Line Protocol、JSON、Bulk VALUES 与 SQL Batch 端点的吞吐差异。

### 对比方案

| 组别 | 对照对象 | 当前状态 |
| --- | --- | --- |
| 嵌入式 | SonnetDB Core | 已实现，`InsertBenchmark.SonnetDB_Insert_1M` |
| 嵌入式 | SQLite | 已实现，`InsertBenchmark.SQLite_Insert_1M` |
| 嵌入式 | LiteDB | 待补，需新增 LiteDB benchmark 依赖与文档化 schema |
| 服务端 | SonnetDB Server | 已实现，`ServerInsertBenchmark` |
| 服务端 | InfluxDB 2.7 | 已实现，Line Protocol 写入 |
| 服务端 | TDengine 3.3.4.3 | 已实现，REST INSERT 与 schemaless LP |
| 服务端 | Apache IoTDB | 待补，需新增 Docker service 与 Session/REST 写入路径 |
| 服务端 | PostgreSQL/TimescaleDB | 待补，需新增 TimescaleDB hypertable 与 COPY/INSERT 基准 |

统一数据集：

- 数据量：1,000,000 点
- 时间间隔：1 秒 1 点
- measurement：`sensor_data`
- tag：`host=server001`
- field：`value FLOAT`
- 随机种子：固定，确保每个数据库输入一致

运行命令：

```powershell
$env:SONNETDB_BENCH_PORT="5081"
$env:SONNETDB_BENCH_URL="http://localhost:5081"
dotnet run -c Release --project tests/SonnetDB.Benchmarks -- --filter *Insert*
dotnet run -c Release --project tests/SonnetDB.Benchmarks -- --filter *ServerInsert*
```

### 对比结果

本轮实测环境：

| 项 | 值 |
| --- | --- |
| 日期 | 2026-04-26 |
| CPU | Intel Core Ultra 9 185H，16C/22T |
| OS | Windows 11 10.0.26200 |
| .NET | SDK 10.0.202，Runtime 10.0.7 |
| Docker | Docker 29.3.1 / Compose v5.1.1 |

| 方法 | 平均耗时 | 分配 | 吞吐 | 备注 |
| --- | ---: | ---: | ---: | --- |
| SonnetDB Core 写入 1M | 704.8 ms | 693.29 MB | 141.9 万点/秒 | 嵌入式基线 |
| SQLite 写入 1M | 1,183.2 ms | 465.40 MB | 84.5 万点/秒 | 嵌入式关系库对照 |
| InfluxDB 写入 1M | 7,392.0 ms | 1,458.95 MB | 13.5 万点/秒 | 服务端 TSDB 对照 |
| TDengine REST INSERT 写入 1M | 16,421.5 ms | 169.33 MB | 6.1 万点/秒 | SQL REST 路径 |
| TDengine schemaless LP 写入 1M | 2,097.0 ms | 61.35 MB | 47.7 万点/秒 | 快速写入路径 |
| SonnetDB Server SQL Batch 写入 1M | 13.469 s | 676.03 MB | 7.4 万点/秒 | HTTP + SQL parser |
| SonnetDB Server LP 写入 1M | 1.651 s | 52.41 MB | 60.6 万点/秒 | HTTP Line Protocol 快路径 |
| SonnetDB Server JSON 写入 1M | 2.309 s | 71.46 MB | 43.3 万点/秒 | HTTP JSON 批量路径 |
| SonnetDB Server Bulk VALUES 写入 1M | 1.691 s | 34.27 MB | 59.1 万点/秒 | HTTP Bulk VALUES 快路径 |

### 结论口径

发布时只使用本轮 CSV/Markdown 报告中的实测值。LiteDB、IoTDB、TimescaleDB 尚未纳入可执行基准前，不在结论里给出性能断言。
