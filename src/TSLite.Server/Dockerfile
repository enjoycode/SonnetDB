# TSLite.Server — Docker 多阶段构建
#
# 从仓库根目录构建：
#   docker build -f src/TSLite.Server/Dockerfile -t tslite-server .
#
# 直接运行：
#   docker run -p 5080:5080 -e TSLITE_TSLiteServer__Tokens__mytoken=admin tslite-server

# ── 构建阶段 ────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# 先复制项目文件以利用 Docker layer 缓存
COPY Directory.Build.props .
COPY Directory.Packages.props .
COPY global.json .
COPY src/TSLite/TSLite.csproj src/TSLite/
COPY src/TSLite.Server/TSLite.Server.csproj src/TSLite.Server/

# restore 依赖
RUN dotnet restore src/TSLite.Server/TSLite.Server.csproj

# 复制剩余源码
COPY src/TSLite/ src/TSLite/
COPY src/TSLite.Server/ src/TSLite.Server/

# 发布（Framework-dependent，不启用 AOT）
RUN dotnet publish src/TSLite.Server/TSLite.Server.csproj \
    -c Release \
    -o /app/publish

# ── 运行阶段 ────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# 数据目录
RUN mkdir -p /data

EXPOSE 5080

# 覆盖 DataRoot 为容器内路径，绑定 5080 端口
ENV TSLITE_TSLiteServer__DataRoot=/data
ENV TSLITE_TSLiteServer__AutoLoadExistingDatabases=true
ENV TSLITE_TSLiteServer__AllowAnonymousProbes=true

ENTRYPOINT ["dotnet", "TSLite.Server.dll"]
