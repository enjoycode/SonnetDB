# 快速启动指南

## 如何运行 SonnetDB vs IoTDB 对比基准测试

当前实现已接入项目入口：

```bash
# 真实小规模冒烟：20 设备 × 30 字段 × 3 时间点
dotnet run -c Release --project tests/SonnetDB.Benchmarks/SonnetDB.Benchmarks.csproj -- --comparison-smoke

# 正式 AB BA AB BA：1,000 设备 × 30 字段 × 12 时间点
dotnet run -c Release --project tests/SonnetDB.Benchmarks/SonnetDB.Benchmarks.csproj -- --comparison

# 高基数完整模式：100,000 设备 × 30 字段 × 12 时间点，可能非常耗时
dotnet run -c Release --project tests/SonnetDB.Benchmarks/SonnetDB.Benchmarks.csproj -- --comparison-full
```

正式默认规模是 12,000 行、360,000 个字段值，吞吐量按 `values/sec` 统计。SonnetDB 与 IoTDB 写入同样的设备、时间戳和 `c1..c30` 字段。

### 步骤 1：准备环境

#### 1.1 启动 IoTDB 容器

```bash
# 在项目根目录运行
docker compose -f SonnetDB/tests/SonnetDB.Benchmarks/docker/docker-compose.yml up -d iotdb

# 验证 IoTDB 是否就绪（HTTP 连接）
curl -s -u root:root http://localhost:18080/rest/v2/query \
  -H "Content-Type: application/json" \
  -d '{"sql":"SHOW VERSION"}' | jq .

# 或用 PowerShell
Invoke-WebRequest -Uri "http://localhost:18080/rest/v2/query" `
  -Method Post `
  -Headers @{"Authorization" = "Basic $(([Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('root:root'))))"} `
  -ContentType "application/json" `
  -Body '{"sql":"SHOW VERSION"}'
```

#### 1.2 编译测试项目

```bash
cd SonnetDB
dotnet build tests/SonnetDB.Benchmarks/SonnetDB.Benchmarks.csproj -c Release
```

### 步骤 2：创建测试运行程序

方式 A：创建独立的控制台应用（推荐）

```bash
# 1. 创建新的控制台应用
dotnet new console -n BenchmarkRunner
cd BenchmarkRunner

# 2. 添加项目引用
dotnet add reference ../SonnetDB/tests/SonnetDB.Benchmarks/SonnetDB.Benchmarks.csproj

# 3. 编辑 Program.cs
```

**Program.cs** 内容：

```csharp
using SonnetDB.Benchmarks.Benchmarks;

Console.WriteLine("正在初始化 SonnetDB vs IoTDB 对比基准测试...");
Console.WriteLine();

try
{
    // 运行对比基准测试
    await DatabaseComparisonBenchmark.RunComparison();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"错误: {ex.Message}");
    Environment.Exit(1);
}
```

```bash
# 4. 运行测试
dotnet run -c Release
```

方式 B：在现有的 BenchmarkDotNet 框架中运行

修改 `SonnetDB/tests/SonnetDB.Benchmarks/Program.cs`：

```csharp
using BenchmarkDotNet.Running;
using SonnetDB.Benchmarks.Benchmarks;

// 检查命令行参数
if (args.Length > 0 && args[0] == "--comparison")
{
    // 运行对比测试
    await DatabaseComparisonBenchmark.RunComparison();
}
else
{
    // 运行 BenchmarkDotNet 测试
    BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
```

然后运行：

```bash
cd SonnetDB
dotnet run -c Release --project tests/SonnetDB.Benchmarks/SonnetDB.Benchmarks.csproj -- --comparison
```

### 步骤 3：运行测试

```bash
# 确保 IoTDB 正在运行
docker ps | grep iotdb

# 运行测试
dotnet run -c Release

# 或如果使用方式 B
dotnet run -c Release -p tests/SonnetDB.Benchmarks/SonnetDB.Benchmarks.csproj -- --comparison
```

## 预期输出示例

```
═════════════════════════════════════════════════════════════════
  SonnetDB vs IoTDB 对比基准测试 (AB BA AB BA 四轮)
═════════════════════════════════════════════════════════════════

╔═══ 第 1 轮：AB ═══╗

● A 阶段开始...
    SonnetDB 进度: 2026-04-01 00:00:00 批次 10/100 | 已写 300,000 点 | 吞吐 156,250 pts/sec
    SonnetDB 进度: 2026-04-01 00:50:00 批次 20/100 | 已写 600,000 点 | 吞吐 147,059 pts/sec
    ...
    SonnetDB 完成: 共写入 288,000,000 点，耗时 2234.56s，吞吐 128,952 pts/sec
  耗时: 2234560ms | 吞吐量: 128952 pts/sec

● B 阶段开始...
    IoTDB 生成进度: 2026-04-01 00:00:00 (10/100 * 1000设备) 已生成 300,000 点
    IoTDB 生成进度: 2026-04-01 00:50:00 (20/100 * 1000设备) 已生成 600,000 点
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
║      1 ║ SonnetDB    ║    2234560 ║   288,000,000 ║             128952 ║
║      1 ║ IoTDB       ║    3456780 ║   288,000,000 ║              83217 ║
║      2 ║ IoTDB       ║    3489123 ║   288,000,000 ║              82574 ║
║      2 ║ SonnetDB    ║    2245678 ║   288,000,000 ║             128267 ║
║      3 ║ SonnetDB    ║    2256789 ║   288,000,000 ║             127661 ║
║      3 ║ IoTDB       ║    3512456 ║   288,000,000 ║              81953 ║
║      4 ║ IoTDB       ║    3498765 ║   288,000,000 ║              82304 ║
║      4 ║ SonnetDB    ║    2267890 ║   288,000,000 ║             127015 ║
╚════════╩═════════════╩════════════╩═══════════════╩═══════════════════╝

● SonnetDB 统计:
  平均耗时: 2251229 ms
  最小耗时: 2234560 ms
  最大耗时: 2267890 ms
  平均吞吐量: 127969 pts/sec

● IoTDB 统计:
  平均耗时: 3489531 ms
  最小耗时: 3456780 ms
  最大耗时: 3512456 ms
  平均吞吐量: 82512 pts/sec

● 相对性能对比:
  SonnetDB 比 IoTDB 快 1.55x
```

## 性能调优建议

为了获得最准确的结果：

```bash
# 1. 关闭不必要的后台服务
sudo systemctl stop \
  avahi-daemon \
  cups \
  bluetooth

# 2. 设置 CPU 频率缩放为性能模式（Linux）
echo performance | sudo tee /sys/devices/system/cpu/cpu*/cpufreq/scaling_governor

# 3. 检查系统负载
top -b -n 1 | head -15

# 4. 运行测试
dotnet run -c Release

# 5. 恢复 CPU 频率缩放
echo ondemand | sudo tee /sys/devices/system/cpu/cpu*/cpufreq/scaling_governor
```

## 清理和清除数据

```bash
# 停止 IoTDB 并删除数据
docker compose -f SonnetDB/tests/SonnetDB.Benchmarks/docker/docker-compose.yml down -v

# 删除 SonnetDB 临时数据
rm -rf /tmp/sonnetdb_bench_*
```

## 故障排除

### IoTDB 连接超时

```bash
# 检查 IoTDB 容器状态
docker ps | grep iotdb

# 查看日志
docker logs sndb-bench-iotdb | tail -50

# 重启 IoTDB
docker compose -f SonnetDB/tests/SonnetDB.Benchmarks/docker/docker-compose.yml restart iotdb
```

### 内存不足错误

减少测试数据量：

编辑 `DatabaseComparisonBenchmark.cs`，修改循环参数：

```csharp
// 从 288 改为 28（减少 90%）
for (var i = 0; i < 28; i++)  // 原为 288

// 从 100 改为 10（再减少 90%）
for (var j = 0; j < 10; j++)  // 原为 100
```

### 网络速度慢

IoTDB 通过 HTTP 通信，网络延迟可能显著影响性能。建议：

1. 使用本地 IoTDB（而非远程）
2. 检查网络连接质量
3. 增加 HTTP 超时时间

## 数据分析

导出结果供进一步分析：

```csharp
// 修改 PrintStatistics 方法，添加 CSV 导出
var csv = string.Join("\n", results.Select(r =>
    $"{r.RunNumber},{r.DatabaseName},{r.TotalMilliseconds},{r.PointsPerSecond:F0}"));
File.WriteAllText("benchmark_results.csv", csv);
```

## 参考资源

- [SonnetDB 文档](https://github.com/IoTSharp/SonnetDB)
- [Apache IoTDB 文档](https://iotdb.apache.org/)
- [基准测试最佳实践](https://github.com/dotnet/performance)

---

有任何问题，请参考 `DATABASE_COMPARISON_BENCHMARK_README.md` 获取详细文档。
