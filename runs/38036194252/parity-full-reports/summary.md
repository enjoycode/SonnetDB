# SonnetDB Parity Summary

| Field | Value |
|---|---|
| Profile | full |
| Status | passing |
| Pass rate | 100% |
| Scenarios | 49 passed / 2 skipped / 0 failed / 51 total |
| Warning-only performance scenarios | 2 |
| Commit | 46dd3a5ec4a7d2e3ae7b07b139289f34c767345f |
| GitHub run | 38036194252 |

## Suites

| Suite | Passed | Skipped | Failed | Total |
|---|---:|---:|---:|---:|
| analytics-c86e349b | 5 | 0 | 0 | 5 |
| document-75de7615 | 5 | 0 | 0 | 5 |
| fulltext-78566abe | 6 | 0 | 0 | 6 |
| graph-871643b8 | 1 | 0 | 0 | 1 |
| kv-2cedee11 | 5 | 0 | 0 | 5 |
| mq-1e49068b | 5 | 0 | 0 | 5 |
| object-1420cb9d | 5 | 0 | 0 | 5 |
| relational-81496145 | 8 | 1 | 0 | 9 |
| tsdb-3d2397b5 | 6 | 1 | 0 | 7 |
| vector-8fab608d | 3 | 0 | 0 | 3 |

## Gate Failures

No capability, reliability, or accuracy gate failures.

## Performance Warnings

| Suite | Scenario | Note |
|---|---|---|
| analytics-c86e349b | groupby_time_1b_rows_wallclock | performance metrics are warning only |
| analytics-c86e349b | columnar_compression_ratio | performance metrics are warning only |
