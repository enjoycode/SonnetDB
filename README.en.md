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
| `src/TSLite.Cli` | `tslite` CLI: local/remote connections, profile management (`local`/`remote`/`connect`), and interactive REPL |
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

## Benchmarks

> Numbers below are from **PR #49** local same-host comparison runs (i9-13900HX / Windows 11 / .NET 10.0.6 / Docker Desktop + WSL2; full suite of 24 benchmarks ~20 min). InfluxDB 2.7, TDengine 3.3.4.3 and `tslite-server` all run in local docker containers; results illustrate relative magnitudes only and do not represent production deployment performance. Full numbers live in [tests/TSLite.Benchmarks/README.md](tests/TSLite.Benchmarks/README.md).

### Insert: 1,000,000 points (single series, IterationCount=3)

| Write path                                              | Mean       | Allocated  | vs TSLite embedded |
|-------------------------------------------------------- |-----------:|-----------:|-------------------:|
| **TSLite embedded** `Tsdb.WriteMany`                    |   544.9 ms |   529.7 MB |             1.00× |
| TSLite Server `POST /v1/.../bulk`                       |    1.120 s |    34.2 MB |             2.06× |
| TSLite Server `POST /v1/.../lp`                         |    1.293 s |    52.4 MB |             2.37× |
| TSLite Server `POST /v1/.../json`                       |    1.352 s |    71.4 MB |             2.48× |
| TSLite Server `POST /v1/.../sql/batch`                  |   19.797 s |   655.5 MB |            36.3× |
| SQLite (file + WAL, single-tx batch INSERT)             |   811.4 ms |   465.4 MB |             1.49× |
| InfluxDB 2.7 (`WriteApiAsync`, 10k/batch LP)            |    5.222 s |  1457.4 MB |             9.58× |
| TDengine 3.3.4.3 REST INSERT (1k/batch, explicit STable)|   44.137 s |   156.1 MB |            81.0× |
| TDengine 3.3.4.3 schemaless LP (`/influxdb/v1/write`)   |   996.0 ms |    61.2 MB |             1.83× |

> TSLite embedded ≈ **1.83 M pts/s**. The three server fast-path endpoints (LP/JSON/Bulk) all sit in the "1M points/s + ≤ 80 MB allocated" band, exceeding the original Milestone 11 goal of ≥ 700k pts/s. `/sql/batch` goes through the SQL parser and is the most expensive path by design.

### Range query: ~100k rows (last 10% time window)

| Engine               | Mean       | Allocated |
|--------------------- |-----------:|----------:|
| **TSLite embedded**  |   6.71 ms  |   18.7 MB |
| TSLite Server (HTTP) |  88.40 ms  |   16.1 MB |
| SQLite               |  44.54 ms  |    9.8 MB |
| InfluxDB 2.7         | 411.13 ms  |  280.5 MB |
| TDengine 3.3.4.3     |  56.29 ms  |   14.0 MB |

### 1-minute bucket aggregate (AVG / MIN / MAX / COUNT, 16,667 buckets)

| Engine               | Mean       | Allocated |
|--------------------- |-----------:|----------:|
| **TSLite embedded**  |  42.26 ms  |   39.4 MB |
| TSLite Server (HTTP) |  88.82 ms  |    2.5 MB |
| SQLite               | 327.29 ms  |    2.5 MB |
| InfluxDB 2.7         |  81.48 ms  |   47.2 MB |
| TDengine 3.3.4.3     |  59.63 ms  |    3.1 MB |

### Highlights

- TSLite write is **9.6×** faster than InfluxDB, **81×** faster than TDengine REST INSERT, and **1.83×** faster than TDengine schemaless LP.
- TSLite range query is **61×** faster than InfluxDB and **6.6×** faster than SQLite.
- The server LP / JSON / Bulk endpoints are **15–17×** faster than `/sql/batch` with **5–11%** of its allocations, and only ~2.0–2.5× more overhead than embedded (HTTP / Kestrel / auth / payload parsing).
- TDengine same-database schemaless LP is **44×** faster and uses **39%** of the allocations of REST INSERT into a sub-table, showing the gap between the schemaless fast path and the SQL parser path.

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
