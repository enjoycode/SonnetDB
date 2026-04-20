# Server Bundle

Server Bundle 面向“下载即启动”的部署场景，默认已经包含：

- `TSLite.Server` 原生可执行文件
- 嵌入式管理前端
- `TSLite.Cli` 原生命令行工具
- `TSLite` / `TSLite.Data` / `TSLite.Cli` 的 NuGet 包
- 本地数据目录 `tslite-data/`
- 默认管理员账号与 Bearer Token
- 启动脚本与使用文档

## 启动方式

Windows：

```powershell
.\start-tslite-server.cmd
```

Linux：

```bash
chmod +x ./start-tslite-server.sh ./tslite
./start-tslite-server.sh
```

启动后可直接访问：

- 管理后台：`http://127.0.0.1:5080/admin`
- 健康检查：`http://127.0.0.1:5080/healthz`
- 指标接口：`http://127.0.0.1:5080/metrics`

## 默认凭据

- 用户名：`admin`
- 密码：`Admin123!`
- Bearer Token：`tslite-admin-token`

## 目录结构

```text
tslite-server-full-0.1.0-<rid>/
├── TSLite.Server(.exe)
├── appsettings.json
├── cli/
├── packages/
├── docs/
├── tslite-data/
├── start-tslite-server.cmd|sh
└── tslite.cmd|tslite
```
