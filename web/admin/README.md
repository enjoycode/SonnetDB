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

- 所有管理动作（创建用户 / GRANT / REVOKE / 数据库 CRUD / SHOW USERS …）**全部走 SQL 端点** `POST /v1/db/{db}/sql`，前端不依赖任何额外 REST 端点。
- 认证：`POST /v1/auth/login` 拿 token → 存 localStorage → axios 拦截器自动加 `Bearer`。
- 路由前缀：`/admin/`（与服务端嵌入挂载点一致），SPA fallback 到 `index.html`。
