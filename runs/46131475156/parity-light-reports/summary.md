# SonnetDB Parity Summary

| Field | Value |
|---|---|
| Profile | light |
| Status | passing |
| Pass rate | 100% |
| Scenarios | 24 passed / 27 skipped / 0 failed / 51 total |
| Warning-only performance scenarios | 2 |
| Commit | 46dd3a5ec4a7d2e3ae7b07b139289f34c767345f |
| GitHub run | 46131475156 |

## Suites

| Suite | Passed | Skipped | Failed | Total |
|---|---:|---:|---:|---:|
| analytics-8738e346 | 0 | 5 | 0 | 5 |
| document-45a12bb8 | 0 | 5 | 0 | 5 |
| fulltext-e94bd942 | 0 | 6 | 0 | 6 |
| graph-be7bf8c0 | 1 | 0 | 0 | 1 |
| kv-f8eba515 | 5 | 0 | 0 | 5 |
| mq-a9d3fd37 | 5 | 0 | 0 | 5 |
| object-1c865a05 | 5 | 0 | 0 | 5 |
| relational-4780b84a | 8 | 1 | 0 | 9 |
| tsdb-d49ee14d | 0 | 7 | 0 | 7 |
| vector-d0f58ebb | 0 | 3 | 0 | 3 |

## Gate Failures

No capability, reliability, or accuracy gate failures.

## Performance Warnings

| Suite | Scenario | Note |
|---|---|---|
| analytics-8738e346 | groupby_time_1b_rows_wallclock | performance metrics are warning only |
| analytics-8738e346 | columnar_compression_ratio | performance metrics are warning only |
