# SonnetDB Parity Summary

| Field | Value |
|---|---|
| Profile | full |
| Status | passing |
| Pass rate | 100% |
| Scenarios | 49 passed / 2 skipped / 0 failed / 51 total |
| Warning-only performance scenarios | 2 |
| Commit | 46dd3a5ec4a7d2e3ae7b07b139289f34c767345f |
| GitHub run | 46131475156 |

## Suites

| Suite | Passed | Skipped | Failed | Total |
|---|---:|---:|---:|---:|
| analytics-1f8c6f1c | 5 | 0 | 0 | 5 |
| document-34620ab9 | 5 | 0 | 0 | 5 |
| fulltext-08d51e1b | 6 | 0 | 0 | 6 |
| graph-ddb07ec5 | 1 | 0 | 0 | 1 |
| kv-5acd175e | 5 | 0 | 0 | 5 |
| mq-9678256e | 5 | 0 | 0 | 5 |
| object-23b05e52 | 5 | 0 | 0 | 5 |
| relational-53129aa1 | 8 | 1 | 0 | 9 |
| tsdb-1e6dec0f | 6 | 1 | 0 | 7 |
| vector-3eee71f0 | 3 | 0 | 0 | 3 |

## Gate Failures

No capability, reliability, or accuracy gate failures.

## Performance Warnings

| Suite | Scenario | Note |
|---|---|---|
| analytics-1f8c6f1c | groupby_time_1b_rows_wallclock | performance metrics are warning only |
| analytics-1f8c6f1c | columnar_compression_ratio | performance metrics are warning only |
