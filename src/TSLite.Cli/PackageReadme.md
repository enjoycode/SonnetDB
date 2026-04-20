# TSLite.Cli

`TSLite.Cli` 是 TSLite 的命令行工具包，安装后命令名为 `tslite`。

## 安装

```bash
dotnet tool install --global TSLite.Cli --version 0.1.0
```

## 示例

```bash
tslite version
tslite sql --connection "Data Source=./demo-data" --command "CREATE MEASUREMENT cpu (host TAG, value FIELD FLOAT)"
tslite sql --connection "Data Source=./demo-data" --command "SELECT count(*) FROM cpu"
tslite repl --connection "Data Source=./demo-data"
```

远程连接示例：

```bash
tslite sql --connection "Data Source=tslite+http://127.0.0.1:5080/metrics;Token=tslite-admin-token" --command "SHOW DATABASES"
```

完整发布产物说明见仓库根目录 `docs/releases/`。
