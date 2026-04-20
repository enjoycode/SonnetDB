# TSLite.Server multi-stage Docker build.
# Build from the repository root:
#   docker build -f src/TSLite.Server/Dockerfile -t tslite-server .
#
# Run:
#   docker run -p 5080:5080 -e TSLITE_TSLiteServer__Tokens__mytoken=admin tslite-server

FROM node:24-bookworm-slim AS frontend-build
WORKDIR /repo/web/admin

COPY web/admin/package.json web/admin/package-lock.json ./
RUN npm ci --no-audit --no-fund

COPY web/admin/index.html web/admin/env.d.ts web/admin/tsconfig.json web/admin/tsconfig.node.json web/admin/vite.config.ts ./
COPY web/admin/public ./public
COPY web/admin/src ./src

RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# Copy project files first to maximize Docker layer reuse for restore.
COPY Directory.Build.props ./
COPY Directory.Packages.props ./
COPY global.json ./
COPY .config/dotnet-tools.json .config/
COPY src/TSLite/TSLite.csproj src/TSLite/
COPY src/TSLite.Server/TSLite.Server.csproj src/TSLite.Server/

RUN dotnet restore src/TSLite.Server/TSLite.Server.csproj
RUN dotnet tool restore

COPY src/TSLite/ src/TSLite/
COPY src/TSLite.Server/ src/TSLite.Server/
COPY docs/ docs/
COPY web/admin/index.html web/admin/env.d.ts web/admin/tsconfig.json web/admin/tsconfig.node.json web/admin/vite.config.ts web/admin/
COPY web/admin/public web/admin/public
COPY --from=frontend-build /repo/web/admin/dist web/admin/dist
RUN rm -rf src/TSLite.Server/wwwroot/help \
    && dotnet tool run jekyllnet build --source docs --destination src/TSLite.Server/wwwroot/help

RUN dotnet publish src/TSLite.Server/TSLite.Server.csproj \
    -c Release \
    -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish ./

RUN mkdir -p /data

EXPOSE 5080

ENV TSLITE_TSLiteServer__DataRoot=/data
ENV TSLITE_TSLiteServer__AutoLoadExistingDatabases=true
ENV TSLITE_TSLiteServer__AllowAnonymousProbes=true

ENTRYPOINT ["dotnet", "TSLite.Server.dll"]
