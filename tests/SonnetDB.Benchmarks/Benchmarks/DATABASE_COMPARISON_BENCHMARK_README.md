# SonnetDB vs IoTDB 对比基准测试

## 简介

这是一个 SonnetDB 与 Apache IoTDB 的性能对比基准测试，用于对两个时序数据库进行性能评估。测试模拟了 10 万设备、每设备 30 个测点、一天数据的写入场景。

> 当前实现口径：SonnetDB 与 IoTDB 都写入相同的设备、时间戳和 `c1..c30` 字段。默认正式规模为 1,000 个设备 × 12 个时间点 = 12,000 行；每行 30 个字段，总计 360,000 个字段值。吞吐量按 `values/sec` 统计，避免把“行”和“字段值”混在一起。
>
> IoTDB 侧使用每个设备一个 aligned timeseries，并通过 REST v2 `insertTablet` 写入同样的 `c1..c30` 字段。

## 功能特性

- **AB BA AB BA 四轮测试**：按照特定顺序运行四轮测试（A=SonnetDB, B=IoTDB）
- **不并行执行**：测试逐次执行，避免并发干扰
- **详细性能指标**：
  - 单次运行耗时（毫秒）
  - 写入数据点总数
  - 吞吐量（数据点/秒）
- **统计分析**：
  - 平均/最小/最大耗时
  - 平均吞吐量
  - 相对性能对比

## 测试数据规模

- **设备数**：1,000 个设备（`--comparison-full` 为 100,000 个设备）
- **测点数/设备**：30 个
- **时间范围**：1 小时（12 个 5 分钟间隔）
- **总行数**：12,000 行
- **总字段值数**：360,000 个字段值

## 环境要求

### 前置条件

1. **SonnetDB**: 本地开发环境（自动启动）
2. **IoTDB**: Docker 容器运行
3. **.NET 10 SDK**: 编译和运行

### 启动外部数据库

```bash
# 启动 IoTDB
docker compose -f tests/SonnetDB.Benchmarks/docker/docker-compose.yml up -d iotdb

# 检查 IoTDB 是否就绪
curl -u root:root http://localhost:18080/rest/v2/query -d '{"sql":"SHOW VERSION"}' -H "Content-Type: application/json"
```

## 使用方法

### 方式一：作为基准测试之一运行

将 `DatabaseComparisonBenchmark` 集成到 BenchmarkDotNet 框架（需要修改 Program.cs）：

```csharp
// Program.cs 中加入
var benchmarks = new[] { typeof(DatabaseComparisonBenchmark) };
BenchmarkRunner.Run(benchmarks);
```

### 方式二：独立程序运行（推荐）

创建一个独立的控制台应用：

```csharp
// 在您的程序中调用
await DatabaseComparisonBenchmark.RunComparison();
```

或参考示例代码：

```csharp
using SonnetDB.Benchmarks.Benchmarks;

// 运行对比测试
await DatabaseComparisonBenchmark.RunComparison();
```

### 编译和运行

```bash
# 编译
cd SonnetDB
dotnet build tests/SonnetDB.Benchmarks/SonnetDB.Benchmarks.csproj -c Release

# 创建测试运行脚本 (Program.cs 或单独项目中)
# 然后运行...
```

## 输出示例

```
═════════════════════════════════════════════════════════════════
  SonnetDB vs IoTDB 对比基准测试 (AB BA AB BA 四轮)
═════════════════════════════════════════════════════════════════

╔═══ 第 1 轮：AB ═══╗

● A 阶段开始...
    SonnetDB 进度: 2026-04-01 00:00:00 批次 10/100 | 已写 300,000 点 | 吞吐 123,456 pts/sec
    SonnetDB 进度: 2026-04-01 00:05:00 批次 20/100 | 已写 600,000 点 | 吞吐 125,000 pts/sec
    ...
    SonnetDB 完成: 共写入 288,000,000 点，耗时 2345.67s，吞吐 122,635 pts/sec
  耗时: 2345670ms | 吞吐量: 122635 pts/sec

● B 阶段开始...
    IoTDB 生成进度: 2026-04-01 00:00:00 (10/100 * 1000设备) 已生成 300,000 点
    ...
    IoTDB 写入完成: 288000000 点，耗时 3456.78 秒
  耗时: 3456780ms | 吞吐量: 83217 pts/sec

...

═════════════════════════════════════════════════════════════════
  性能对比总结
═════════════════════════════════════════════════════════════════

╔════════╦═════════════╦════════════╦═══════════════╦═══════════════════╗
║ 轮数   ║ 数据库      ║ 耗时(ms)   ║ 数据点        ║ 吞吐量(pts/sec)   ║
╠════════╬═════════════╬════════════╬═══════════════╬═══════════════════╣
║      1 ║ SonnetDB    ║    2345670 ║   288,000,000 ║             122635 ║
║      1 ║ IoTDB       ║    3456780 ║   288,000,000 ║              83217 ║
╚════════╩═════════════╩════════════╩═══════════════╩═══════════════════╝

● SonnetDB 统计:
  平均耗时: 2400000 ms
  最小耗时: 2345670 ms
  最大耗时: 2450000 ms
  平均吞吐量: 121453 pts/sec

● IoTDB 统计:
  平均耗时: 3500000 ms
  最小耗时: 3456780 ms
  最大耗时: 3520000 ms
  平均吞吐量: 82857 pts/sec

● 相对性能对比:
  SonnetDB 比 IoTDB 快 1.46x
```

## 代码位置

- 测试类：[DatabaseComparisonBenchmark.cs](./Benchmarks/DatabaseComparisonBenchmark.cs)
- 所需库：
  - SonnetDB.Benchmarks.Helpers.IoTDBRestClient
  - SonnetDB.Benchmarks.Helpers.BenchmarkDataPoint

## 性能测试指标说明

| 指标 | 说明 |
|------|------|
| 耗时(ms) | 测试运行的总耗时，单位毫秒 |
| 数据点 | 写入的总数据点数 |
| 吞吐量(pts/sec) | 每秒写入的数据点数 = 数据点 × 1000 / 耗时(ms) |
| 平均耗时 | 四轮测试中相同数据库的平均耗时 |
| 相对性能对比 | SonnetDB吞吐量 / IoTDB吞吐量 |

## 常见问题

### Q: IoTDB 连接失败

**A**: 确保 IoTDB 已启动：
```bash
docker ps | grep iotdb
# 如果未启动，运行：
docker compose -f tests/SonnetDB.Benchmarks/docker/docker-compose.yml up -d iotdb
```

### Q: 测试耗时很长

**A**: 这是正常的。测试涉及 2.88 亿条数据点的写入，通常需要 30 分钟到几小时，具体取决于硬件性能。

### Q: 可以减少测试数据量吗？

**A**: 可以。修改 `RunSonnetDbBenchmark()` 和 `RunIoTDbBenchmarkAsync()` 中的循环次数：
- 修改 `for (var i = 0; i < 288; i++)` 为 `for (var i = 0; i < 28; i++)` 可减少 90% 的数据
- 修改 `for (var j = 0; j < 100; j++)` 为 `for (var j = 0; j < 10; j++)` 可减少 90% 的数据

### Q: 如何导出测试结果？

**A**: 可以修改 `PrintStatistics()` 方法，将结果导出为 CSV 或 JSON 格式。

## 注意事项

1. **数据清理**：每轮测试前会清空旧数据，确保测试的独立性
2. **临时文件**：SonnetDB 使用系统临时目录存储数据，测试完成后会自动清理
3. **网络延迟**：IoTDB 通过 HTTP REST API 通信，网络延迟可能影响结果
4. **资源占用**：测试过程会占用大量 CPU 和 内存，建议在专用测试机上运行

## 扩展建议

- 添加 TDengine、InfluxDB 等其他数据库的对比
- 支持自定义测试参数（设备数、测点数等）
- 添加读取性能测试
- 集成测试结果历史跟踪和趋势分析

## 许可证

本测试代码遵循 SonnetDB 开源许可证。
