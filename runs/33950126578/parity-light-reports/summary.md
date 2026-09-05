# SonnetDB Parity Summary

| Field | Value |
|---|---|
| Profile | light |
| Status | passing |
| Pass rate | 100% |
| Scenarios | 24 passed / 27 skipped / 0 failed / 51 total |
| Warning-only performance scenarios | 2 |
| Commit | f2cad5af8b33a3473ada3d3e7047f75a1e8f5f5f |
| GitHub run | 33950126578 |

## Suites

| Suite | Passed | Skipped | Failed | Total |
|---|---:|---:|---:|---:|
| analytics-09696a3e | 0 | 5 | 0 | 5 |
| document-56f2a956 | 0 | 5 | 0 | 5 |
| fulltext-bb0f3807 | 0 | 6 | 0 | 6 |
| graph-98239f68 | 1 | 0 | 0 | 1 |
| kv-4fca9d90 | 5 | 0 | 0 | 5 |
| mq-0c0ba836 | 5 | 0 | 0 | 5 |
| object-caff2c6a | 5 | 0 | 0 | 5 |
| relational-f20e29b1 | 8 | 1 | 0 | 9 |
| tsdb-b058827f | 0 | 7 | 0 | 7 |
| vector-dfded386 | 0 | 3 | 0 | 3 |

## Gate Failures

No capability, reliability, or accuracy gate failures.

## Performance Warnings

| Suite | Scenario | Note |
|---|---|---|
| analytics-09696a3e | groupby_time_1b_rows_wallclock | performance metrics are warning only |
| analytics-09696a3e | columnar_compression_ratio | performance metrics are warning only |
