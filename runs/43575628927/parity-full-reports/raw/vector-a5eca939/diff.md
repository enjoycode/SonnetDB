# Parity Run vector-a5eca939

Started: 2026-11-14T08:33:14.5633229+00:00

| Scenario | sonnetdb | qdrant | Diff |
|---|---|---|---|
| ann_recall_at_10 | ✅ pass (rows=1) | ✅ pass (rows=1) | ✅ within tolerance |
| filtered_search | ✅ pass (rows=1) | ✅ pass (rows=1) | ✅ within tolerance |
| upsert_during_query | ✅ pass (rows=1) | ✅ pass (rows=1) | ✅ within tolerance |

## Capability gaps

| Scenario | Required | sonnetdb | qdrant | SonnetDB gap |
|---|---|---|---|---|
| ann_recall_at_10 | Vector | pass | pass |  |
| filtered_search | Vector, HnswFiltered | pass | pass |  |
| upsert_during_query | Vector | pass | pass |  |
