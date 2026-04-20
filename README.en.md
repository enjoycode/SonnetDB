# TSLite

[中文](README.md) | [English](README.en.md)

[![CI](https://github.com/maikebing/TSLite/actions/workflows/ci.yml/badge.svg)](https://github.com/maikebing/TSLite/actions/workflows/ci.yml)
[![CodeQL](https://github.com/maikebing/TSLite/actions/workflows/codeql.yml/badge.svg)](https://github.com/maikebing/TSLite/actions/workflows/codeql.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)

TSLite is a time-series database project built with C# and .NET 10. It can run as an embedded engine inside your process, and it can also be deployed through `TSLite.Server` with HTTP APIs, an admin UI, and built-in help docs.

The current persistence model is directory-based. A database is stored as a set of files such as schema, catalog, WAL, segments, and tombstones. It is no longer described as a single-file database.

## What Is Included

| Component | Purpose |
| --- | --- |
| `src/TSLite` | Embedded engine: schema, writes, queries, deletes, WAL, MemTable, Segment, compaction, retention |
| `src/TSLite.Data` | ADO.NET provider for both embedded and remote modes |
| `src/TSLite.Cli` | `tslite` CLI with `version`, `sql`, and `repl` |
| `src/TSLite.Server` | HTTP server, first-run setup, auth/RBAC, SSE, admin UI, `/help` docs |
| `web/admin` | Admin frontend |
| `docs` | JekyllNet documentation site source, bundled into the Docker image |

## Current Capabilities

- Embedded usage through `Tsdb.Open(...)`
- Explicit measurement schema with `CREATE MEASUREMENT`
- SQL writes with `INSERT`, reads with `SELECT`, deletes with `DELETE`
- Aggregates with `count`, `sum`, `min`, `max`, `avg`, `first`, `last`
- Time-bucket aggregation with `GROUP BY time(...)`
- ADO.NET access for local and remote deployments
- CLI access for scripting and ad hoc SQL
- Bulk ingest fast paths through `CommandType.TableDirect` and HTTP bulk endpoints
- Server control-plane SQL for users, databases, grants, and tokens

## Quick Start

### Embedded

```csharp
using TSLite.Engine;
using TSLite.Sql.Execution;

using var db = Tsdb.Open(new TsdbOptions
{
    RootDirectory = "./demo-data",
});

SqlExecutor.Execute(db, """
CREATE MEASUREMENT cpu (
    host TAG,
    usage FIELD FLOAT
)
""");

SqlExecutor.Execute(db, """
INSERT INTO cpu (time, host, usage)
VALUES (1713676800000, 'server-01', 0.71)
""");

var result = (SelectExecutionResult)SqlExecutor.Execute(
    db,
    "SELECT time, host, usage FROM cpu WHERE host = 'server-01'")!;
```

### Server

```bash
docker build -f src/TSLite.Server/Dockerfile -t tslite-server .
docker run --rm -p 5080:5080 -v ./tslite-data:/data tslite-server
```

Then open:

- `http://127.0.0.1:5080/admin/`
- `http://127.0.0.1:5080/help/`

If `/data/.system` is empty, `/admin/` will guide you through the first-run setup flow for:

- server ID
- organization
- admin username
- admin password
- initial static Bearer token

### ADO.NET

```csharp
using TSLite.Data;

using var connection = new TsdbConnection("Data Source=./demo-data");
connection.Open();
```

Remote mode:

```csharp
using TSLite.Data;

using var connection = new TsdbConnection(
    "Data Source=tslite+http://127.0.0.1:5080/metrics;Token=your-token");
connection.Open();
```

## Data Model

| Concept | Meaning |
| --- | --- |
| `measurement` | A time-series entity such as `cpu`, `memory`, or `meter_reading` |
| `tag` | A series identity/filter dimension such as `host`, `region`, or `device_id` |
| `field` | An observed value such as `usage`, `temperature`, or `status` |
| `time` | Reserved timestamp column used in writes, queries, and deletes |
| `series` | Canonical `measurement + sorted(tags)` identity |

Field types currently supported:

- `FLOAT`
- `INT`
- `BOOL`
- `STRING`

## SQL Surface

Data-plane SQL currently supported:

- `CREATE MEASUREMENT`
- `INSERT INTO ... VALUES (...)`
- `SELECT ... FROM ... [WHERE ...] [GROUP BY time(...)]`
- `DELETE FROM ... WHERE ...`

Server control-plane SQL currently supported:

- `CREATE USER`
- `ALTER USER ... WITH PASSWORD`
- `DROP USER`
- `CREATE DATABASE`
- `DROP DATABASE`
- `GRANT READ|WRITE|ADMIN ON DATABASE ... TO ...`
- `REVOKE ON DATABASE ... FROM ...`
- `SHOW USERS`
- `SHOW GRANTS [FOR user]`
- `SHOW DATABASES`
- `SHOW TOKENS [FOR user]`
- `ISSUE TOKEN FOR user`
- `REVOKE TOKEN 'tok_xxx'`

## Architecture At A Glance

```text
Application / CLI / ADO.NET / Admin UI
                |
                v
      SQL / Remote HTTP / TableDirect
                |
                v
     Query Engine / Control Plane / Auth
                |
                v
  WAL -> MemTable -> Flush -> Segment -> Compaction
```

Actual on-disk database layout:

```text
<database-root>/
├─ catalog.tslcat
├─ measurements.tslschema
├─ tombstones.tslmanifest
├─ wal/
│  └─ {startLsn:X16}.tslwal
└─ segments/
   └─ {id:X16}.tslseg
```

Server control-plane layout:

```text
<data-root>/.system/
├─ installation.json
├─ users.json
└─ grants.json
```

## Design Principles

- Safe-only core: no `unsafe` in the current core implementation stage
- Schema-first measurements
- Embedded-first engine with optional server deployment
- Directory-based persistence instead of single-file storage
- Shared API surface across embedded, ADO.NET, CLI, and remote usage
- Docs built into the Docker image through JekyllNet

## Docs

Detailed usage examples live under `docs/`:

- [Getting Started](docs/getting-started.md)
- [Data Model](docs/data-model.md)
- [SQL Reference](docs/sql-reference.md)
- [Embedded and In-Proc API](docs/embedded-api.md)
- [ADO.NET Reference](docs/ado-net.md)
- [CLI Reference](docs/cli-reference.md)
- [Bulk Ingest](docs/bulk-ingest.md)
- [Architecture Overview](docs/architecture.md)
- [File Layout](docs/file-format.md)
- [Release Docs](docs/releases/README.md)

## Related Files

- [ROADMAP.md](ROADMAP.md)
- [CHANGELOG.md](CHANGELOG.md)
- [AGENTS.md](AGENTS.md)

## License

[MIT](LICENSE)
