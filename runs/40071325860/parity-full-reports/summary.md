# SonnetDB Parity Summary

| Field | Value |
|---|---|
| Profile | full |
| Status | passing |
| Pass rate | 100% |
| Scenarios | 49 passed / 2 skipped / 0 failed / 51 total |
| Warning-only performance scenarios | 2 |
| Commit | 46dd3a5ec4a7d2e3ae7b07b139289f34c767345f |
| GitHub run | 40071325860 |

## Suites

| Suite | Passed | Skipped | Failed | Total |
|---|---:|---:|---:|---:|
| analytics-c73dd2e4 | 5 | 0 | 0 | 5 |
| document-ff3c7c86 | 5 | 0 | 0 | 5 |
| fulltext-9347b497 | 6 | 0 | 0 | 6 |
| graph-05f33eed | 1 | 0 | 0 | 1 |
| kv-80ce926f | 5 | 0 | 0 | 5 |
| mq-4b3aae01 | 5 | 0 | 0 | 5 |
| object-ce7b0390 | 5 | 0 | 0 | 5 |
| relational-1d48c00b | 8 | 1 | 0 | 9 |
| tsdb-16c3351f | 6 | 1 | 0 | 7 |
| vector-b18fd816 | 3 | 0 | 0 | 3 |

## Gate Failures

No capability, reliability, or accuracy gate failures.

## Performance Warnings

| Suite | Scenario | Note |
|---|---|---|
| analytics-c73dd2e4 | groupby_time_1b_rows_wallclock | performance metrics are warning only |
| analytics-c73dd2e4 | columnar_compression_ratio | performance metrics are warning only |
