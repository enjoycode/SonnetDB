# SonnetDB multi-stage Docker build.
# Build from the repository root:
#   docker build -f src/SonnetDB/Dockerfile -t iotsharp/sonnetdb .
#
# Run:
#   docker run -p 5080:5080 -e SONNETDB_SonnetDBServer__Tokens__mytoken=admin iotsharp/sonnetdb

FROM node:24-bookworm-slim AS frontend-build
WORKDIR /repo/web

COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund

COPY web/index.html web/env.d.ts web/tsconfig.json web/tsconfig.node.json web/vite.config.ts ./
COPY web/public ./public
COPY web/src ./src

RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# Copy build config and .csproj files first to maximize Docker layer cache for restore.
COPY Directory.Build.props Directory.Packages.props global.json ./
COPY .config/dotnet-tools.json .config/
COPY src/SonnetDB/SonnetDB.csproj src/SonnetDB/
COPY src/SonnetDB.Core/SonnetDB.Core.csproj src/SonnetDB.Core/

RUN dotnet restore src/SonnetDB/SonnetDB.csproj
RUN dotnet tool restore

# Copy full source and assets needed for the actual build.
COPY src/SonnetDB/ src/SonnetDB/
COPY src/SonnetDB.Core/ src/SonnetDB.Core/
COPY docs/ docs/
COPY --from=frontend-build /repo/web/dist web/dist

RUN rm -rf src/SonnetDB/wwwroot/help \
    && dotnet tool run jekyllnet build --source docs --destination src/SonnetDB/wwwroot/help

RUN dotnet publish src/SonnetDB/SonnetDB.csproj \
    -c Release \
    -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish ./

RUN mkdir -p /data

EXPOSE 5080

ENV SONNETDB_SonnetDBServer__DataRoot=/data
ENV SONNETDB_SonnetDBServer__AutoLoadExistingDatabases=true
ENV SONNETDB_SonnetDBServer__AllowAnonymousProbes=true

ENTRYPOINT ["dotnet", "SonnetDB.dll"]
