# SonnetDB Parity Summary

| Field | Value |
|---|---|
| Profile | light |
| Status | passing |
| Pass rate | 100% |
| Scenarios | 24 passed / 27 skipped / 0 failed / 51 total |
| Warning-only performance scenarios | 2 |
| Commit | 46dd3a5ec4a7d2e3ae7b07b139289f34c767345f |
| GitHub run | 42353949313 |

## Suites

| Suite | Passed | Skipped | Failed | Total |
|---|---:|---:|---:|---:|
| analytics-003c108c | 0 | 5 | 0 | 5 |
| document-e9e7017a | 0 | 5 | 0 | 5 |
| fulltext-cc46df58 | 0 | 6 | 0 | 6 |
| graph-bd66c0d1 | 1 | 0 | 0 | 1 |
| kv-e46caed9 | 5 | 0 | 0 | 5 |
| mq-2d0affc0 | 5 | 0 | 0 | 5 |
| object-14a596ad | 5 | 0 | 0 | 5 |
| relational-3ef72b2e | 8 | 1 | 0 | 9 |
| tsdb-e4a4f9b3 | 0 | 7 | 0 | 7 |
| vector-4c51c0b8 | 0 | 3 | 0 | 3 |

## Gate Failures

No capability, reliability, or accuracy gate failures.

## Performance Warnings

| Suite | Scenario | Note |
|---|---|---|
| analytics-003c108c | groupby_time_1b_rows_wallclock | performance metrics are warning only |
| analytics-003c108c | columnar_compression_ratio | performance metrics are warning only |
