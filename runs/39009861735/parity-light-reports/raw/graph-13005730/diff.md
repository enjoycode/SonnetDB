# Parity Run graph-13005730

Started: 2026-10-17T06:31:13.8446324+00:00

| Scenario | sonnetdb | postgres | neo4j | Diff |
|---|---|---|---|---|
| graph_sql_pgq_beta | ✅ pass (rows=1) | not_run | not_run | n/a (single backend) |

## Capability gaps

| Scenario | Required | sonnetdb | postgres | neo4j | SonnetDB gap |
|---|---|---|---|---|---|
| graph_sql_pgq_beta | Graph, GraphSqlPgq, GraphNativeTraversal, GraphCrossModelSql | pass | not_run | not_run |  |
