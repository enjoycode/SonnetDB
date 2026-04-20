# TSLite Admin UI

基于 **Vite + Vue 3 + TypeScript + Naive UI + Pinia + Vue Router** 的单页应用。

构建产物 (`dist/`) 在 `dotnet build` 时通过 MSBuild target 自动生成（如检测到 `npm` 可用），并以 `EmbeddedResource` 形式打包进 `TSLite.Server.dll`，运行时由服务器在 `/admin/*` 路由下托管。

## 本地开发

```bash
cd web/admin
npm install
npm run dev          # http://localhost:5173 （API 反向代理到 :5000）
```

需要先启动后端：

```bash
dotnet run --project src/TSLite.Server -- --Kestrel:Endpoints:Http:Url=http://localhost:5000
```

## 生产构建（手动）

```bash
cd web/admin
npm install
npm run build        # 输出到 web/admin/dist/
```

之后运行 `dotnet build src/TSLite.Server`，dist 会被嵌入。

## 设计要点

- 控制面管理动作通过 SQL 端点完成：admin 走 `POST /v1/sql` 执行 `CREATE USER` / `GRANT` / `ISSUE TOKEN` 等；数据面 SQL 走 `POST /v1/db/{db}/sql`。
- 数据库列表与状态展示复用 `GET /v1/db` 和 `GET /metrics`，这样普通已登录用户也能查看数据库概览，而不必依赖 admin-only 控制面端点。
- 认证：`POST /v1/auth/login` 拿 token → 存 localStorage → axios 拦截器自动加 `Bearer`。
- 路由前缀：`/admin/`（与服务端嵌入挂载点一致），SPA fallback 到 `index.html`。
