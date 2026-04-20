# SDK Bundle

SDK Bundle 面向开发者，目标是“一次下载，直接拥有三套包和本地 CLI”。

## 包含内容

- `packages/TSLite.0.1.0.nupkg`
- `packages/TSLite.Data.0.1.0.nupkg`
- `packages/TSLite.Cli.0.1.0.nupkg`
- `cli/` 原生命令行工具
- `docs/` 使用说明
- `LICENSE`

## 使用方式

NuGet 包：

```bash
dotnet add package TSLite --version 0.1.0
dotnet add package TSLite.Data --version 0.1.0
dotnet tool install --global TSLite.Cli --version 0.1.0
```

本地 CLI：

Windows：

```powershell
.\tslite.cmd version
.\tslite.cmd sql --connection "Data Source=./demo-data" --command "SELECT count(*) FROM cpu"
```

Linux：

```bash
./tslite version
./tslite sql --connection "Data Source=./demo-data" --command "SELECT count(*) FROM cpu"
```
