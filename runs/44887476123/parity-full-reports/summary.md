# SonnetDB Parity Summary

| Field | Value |
|---|---|
| Profile | full |
| Status | passing |
| Pass rate | 100% |
| Scenarios | 49 passed / 2 skipped / 0 failed / 51 total |
| Warning-only performance scenarios | 2 |
| Commit | 46dd3a5ec4a7d2e3ae7b07b139289f34c767345f |
| GitHub run | 44887476123 |

## Suites

| Suite | Passed | Skipped | Failed | Total |
|---|---:|---:|---:|---:|
| analytics-e15021a0 | 5 | 0 | 0 | 5 |
| document-119f41fb | 5 | 0 | 0 | 5 |
| fulltext-0823e248 | 6 | 0 | 0 | 6 |
| graph-59abcfd6 | 1 | 0 | 0 | 1 |
| kv-aba482f5 | 5 | 0 | 0 | 5 |
| mq-f44a2f61 | 5 | 0 | 0 | 5 |
| object-4740da25 | 5 | 0 | 0 | 5 |
| relational-33211bac | 8 | 1 | 0 | 9 |
| tsdb-302598a1 | 6 | 1 | 0 | 7 |
| vector-c76cb81d | 3 | 0 | 0 | 3 |

## Gate Failures

No capability, reliability, or accuracy gate failures.

## Performance Warnings

| Suite | Scenario | Note |
|---|---|---|
| analytics-e15021a0 | groupby_time_1b_rows_wallclock | performance metrics are warning only |
| analytics-e15021a0 | columnar_compression_ratio | performance metrics are warning only |
