# SonnetDB multi-stage Docker build.
# Build from the repository root:
#   docker build -f src/SonnetDB/Dockerfile -t sonnetdb .
#
# Run:
#   docker run -p 5080:5080 -e SONNETDB_SonnetDBServer__Tokens__mytoken=admin sonnetdb

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
COPY src/SonnetDB/SonnetDB.csproj src/SonnetDB/
COPY src/SonnetDB/SonnetDB.csproj src/SonnetDB/

RUN dotnet restore src/SonnetDB/SonnetDB.csproj
RUN dotnet tool restore

COPY src/SonnetDB/ src/SonnetDB/
COPY src/SonnetDB/ src/SonnetDB/
COPY docs/ docs/
COPY web/admin/index.html web/admin/env.d.ts web/admin/tsconfig.json web/admin/tsconfig.node.json web/admin/vite.config.ts web/admin/
COPY web/admin/public web/admin/public
COPY --from=frontend-build /repo/web/admin/dist web/admin/dist
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
