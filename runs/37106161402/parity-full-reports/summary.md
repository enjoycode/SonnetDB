# SonnetDB Parity Summary

| Field | Value |
|---|---|
| Profile | full |
| Status | passing |
| Pass rate | 100% |
| Scenarios | 49 passed / 2 skipped / 0 failed / 51 total |
| Warning-only performance scenarios | 2 |
| Commit | 46dd3a5ec4a7d2e3ae7b07b139289f34c767345f |
| GitHub run | 37106161402 |

## Suites

| Suite | Passed | Skipped | Failed | Total |
|---|---:|---:|---:|---:|
| analytics-a2624777 | 5 | 0 | 0 | 5 |
| document-17580b29 | 5 | 0 | 0 | 5 |
| fulltext-0b626e0d | 6 | 0 | 0 | 6 |
| graph-c8e1444b | 1 | 0 | 0 | 1 |
| kv-ee40eac4 | 5 | 0 | 0 | 5 |
| mq-df49420e | 5 | 0 | 0 | 5 |
| object-65b67d14 | 5 | 0 | 0 | 5 |
| relational-458b8cf2 | 8 | 1 | 0 | 9 |
| tsdb-7c5d34f1 | 6 | 1 | 0 | 7 |
| vector-32d49a7a | 3 | 0 | 0 | 3 |

## Gate Failures

No capability, reliability, or accuracy gate failures.

## Performance Warnings

| Suite | Scenario | Note |
|---|---|---|
| analytics-a2624777 | groupby_time_1b_rows_wallclock | performance metrics are warning only |
| analytics-a2624777 | columnar_compression_ratio | performance metrics are warning only |
