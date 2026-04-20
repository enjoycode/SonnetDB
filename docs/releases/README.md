# TSLite 0.1.0 发布说明

`TSLite 0.1.0` 的发布产物分为 4 类：

| 类型 | 产物 | 说明 |
|------|------|------|
| NuGet | `TSLite.0.1.0.nupkg` | 核心嵌入式时序引擎 |
| NuGet | `TSLite.Data.0.1.0.nupkg` | ADO.NET 提供程序，支持本地与远程连接 |
| NuGet / Tool | `TSLite.Cli.0.1.0.nupkg` | `dotnet tool` 命令行工具，命令名 `tslite` |
| SDK Bundle | `tslite-sdk-0.1.0-win-x64.zip` / `tslite-sdk-0.1.0-linux-x64.tar.gz` | 含三套 NuGet 包、原生命令行工具与使用文档 |
| Server Bundle | `tslite-server-full-0.1.0-win-x64.zip` / `tslite-server-full-0.1.0-linux-x64.tar.gz` | 含 `TSLite.Server`、内置前端、CLI、NuGet 包、默认本地启动配置 |
| Installer | `tslite-server-0.1.0-win-x64.msi` | Windows 可安装版本 |
| Installer | `tslite-server-0.1.0-linux-x64.deb` | Debian / Ubuntu 可安装版本 |
| Installer | `tslite-server-0.1.0-linux-x64.rpm` | RHEL / CentOS / Fedora 可安装版本 |

## 默认一键启动信息

`TSLite.Server` 全量包与安装包都内置了本地演示用启动配置：

- 管理后台地址：`http://127.0.0.1:5080/admin`
- 默认管理员：`admin`
- 默认密码：`Admin123!`
- 默认 Bearer Token：`tslite-admin-token`

这些默认凭据只用于本地开箱即用场景，生产环境请在首次启动后立即修改。

## 相关文档

- [SDK Bundle 说明](./sdk-bundle.md)
- [Server Bundle 说明](./server-bundle.md)
- [安装包说明](./installers.md)
