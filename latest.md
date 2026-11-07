# SonnetDB Parity Summary

| Field | Value |
|---|---|
| Profile | full |
| Status | passing |
| Pass rate | 100% |
| Scenarios | 49 passed / 2 skipped / 0 failed / 51 total |
| Warning-only performance scenarios | 2 |
| Commit | 46dd3a5ec4a7d2e3ae7b07b139289f34c767345f |
| GitHub run | 42353949313 |

## Suites

| Suite | Passed | Skipped | Failed | Total |
|---|---:|---:|---:|---:|
| analytics-97afb24c | 5 | 0 | 0 | 5 |
| document-2affa3ba | 5 | 0 | 0 | 5 |
| fulltext-88919e9d | 6 | 0 | 0 | 6 |
| graph-40f86a21 | 1 | 0 | 0 | 1 |
| kv-ac6797dc | 5 | 0 | 0 | 5 |
| mq-8996494e | 5 | 0 | 0 | 5 |
| object-55b73229 | 5 | 0 | 0 | 5 |
| relational-e70a72b4 | 8 | 1 | 0 | 9 |
| tsdb-4017e564 | 6 | 1 | 0 | 7 |
| vector-97780da1 | 3 | 0 | 0 | 3 |

## Gate Failures

No capability, reliability, or accuracy gate failures.

## Performance Warnings

| Suite | Scenario | Note |
|---|---|---|
| analytics-97afb24c | groupby_time_1b_rows_wallclock | performance metrics are warning only |
| analytics-97afb24c | columnar_compression_ratio | performance metrics are warning only |
