using BenchmarkDotNet.Running;
using SonnetDB.Benchmarks.Benchmarks;

// BenchmarkDotNet 需要在 Release 模式下运行。
// 使用示例：
//   dotnet run -c Release -- --filter *Insert*
//   dotnet run -c Release -- --filter *Query*
//   dotnet run -c Release -- --filter *Aggregate*
//   dotnet run -c Release -- --filter *Compaction*
//   dotnet run -c Release -- --filter *Vector*
//   dotnet run -c Release -- --filter *         （运行所有基准）
//
// 运行前请先启动外部数据库（见 docker/docker-compose.yml）：
//   docker compose -f tests/SonnetDB.Benchmarks/docker/docker-compose.yml up -d
if (args.Contains("--comparison-smoke", StringComparer.OrdinalIgnoreCase))
{
	await DatabaseComparisonBenchmark.RunSmokeComparison().ConfigureAwait(false);
	return;
}

if (args.Contains("--comparison-server-smoke", StringComparer.OrdinalIgnoreCase))
{
	await DatabaseComparisonBenchmark.RunServerSmokeComparison().ConfigureAwait(false);
	return;
}

if (args.Contains("--comparison-full", StringComparer.OrdinalIgnoreCase))
{
	await DatabaseComparisonBenchmark.RunFullComparison().ConfigureAwait(false);
	return;
}

if (args.Contains("--comparison", StringComparer.OrdinalIgnoreCase))
{
	await DatabaseComparisonBenchmark.RunComparison().ConfigureAwait(false);
	return;
}

if (args.Contains("--comparison-server", StringComparer.OrdinalIgnoreCase))
{
	await DatabaseComparisonBenchmark.RunServerComparison().ConfigureAwait(false);
	return;
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
