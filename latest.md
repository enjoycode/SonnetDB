# SonnetDB Parity Summary

| Field | Value |
|---|---|
| Profile | full |
| Status | passing |
| Pass rate | 100% |
| Scenarios | 49 passed / 2 skipped / 0 failed / 51 total |
| Warning-only performance scenarios | 2 |
| Commit | 46dd3a5ec4a7d2e3ae7b07b139289f34c767345f |
| GitHub run | 39009861735 |

## Suites

| Suite | Passed | Skipped | Failed | Total |
|---|---:|---:|---:|---:|
| analytics-2a4219d8 | 5 | 0 | 0 | 5 |
| document-fa1e0084 | 5 | 0 | 0 | 5 |
| fulltext-2e5e08fd | 6 | 0 | 0 | 6 |
| graph-bb03096f | 1 | 0 | 0 | 1 |
| kv-77355187 | 5 | 0 | 0 | 5 |
| mq-cd5814a3 | 5 | 0 | 0 | 5 |
| object-fbea6a1a | 5 | 0 | 0 | 5 |
| relational-38d47309 | 8 | 1 | 0 | 9 |
| tsdb-c4a5c221 | 6 | 1 | 0 | 7 |
| vector-f7c9c3c8 | 3 | 0 | 0 | 3 |

## Gate Failures

No capability, reliability, or accuracy gate failures.

## Performance Warnings

| Suite | Scenario | Note |
|---|---|---|
| analytics-2a4219d8 | groupby_time_1b_rows_wallclock | performance metrics are warning only |
| analytics-2a4219d8 | columnar_compression_ratio | performance metrics are warning only |
