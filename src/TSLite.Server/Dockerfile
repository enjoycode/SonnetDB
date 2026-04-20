# TSLite.Server multi-stage Docker build.
# Build from the repository root:
#   docker build -f src/TSLite.Server/Dockerfile -t tslite-server .
#
# Run:
#   docker run -p 5080:5080 -e TSLITE_TSLiteServer__Tokens__mytoken=admin tslite-server

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

RUN apt-get update \
    && apt-get install -y --no-install-recommends nodejs npm \
    && rm -rf /var/lib/apt/lists/*

# Copy project files first to maximize Docker layer reuse for restore.
COPY Directory.Build.props ./
COPY Directory.Packages.props ./
COPY global.json ./
COPY src/TSLite/TSLite.csproj src/TSLite/
COPY src/TSLite.Server/TSLite.Server.csproj src/TSLite.Server/
COPY web/admin/package.json web/admin/package-lock.json web/admin/

RUN dotnet restore src/TSLite.Server/TSLite.Server.csproj
RUN cd web/admin && npm ci --no-audit --no-fund

COPY src/TSLite/ src/TSLite/
COPY src/TSLite.Server/ src/TSLite.Server/
COPY web/admin/index.html web/admin/env.d.ts web/admin/tsconfig.json web/admin/tsconfig.node.json web/admin/vite.config.ts web/admin/
COPY web/admin/public web/admin/public
COPY web/admin/src web/admin/src

RUN cd web/admin && npm run build

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
