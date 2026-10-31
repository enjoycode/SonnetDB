# SonnetDB Parity Summary

| Field | Value |
|---|---|
| Profile | full |
| Status | passing |
| Pass rate | 100% |
| Scenarios | 49 passed / 2 skipped / 0 failed / 51 total |
| Warning-only performance scenarios | 2 |
| Commit | 46dd3a5ec4a7d2e3ae7b07b139289f34c767345f |
| GitHub run | 41182118364 |

## Suites

| Suite | Passed | Skipped | Failed | Total |
|---|---:|---:|---:|---:|
| analytics-c2f1b4eb | 5 | 0 | 0 | 5 |
| document-ebe2d67c | 5 | 0 | 0 | 5 |
| fulltext-492095e2 | 6 | 0 | 0 | 6 |
| graph-27197a0b | 1 | 0 | 0 | 1 |
| kv-6b5bfc83 | 5 | 0 | 0 | 5 |
| mq-135c1fb1 | 5 | 0 | 0 | 5 |
| object-ed8b27d8 | 5 | 0 | 0 | 5 |
| relational-5bf1e41c | 8 | 1 | 0 | 9 |
| tsdb-2dd646a5 | 6 | 1 | 0 | 7 |
| vector-f62536b7 | 3 | 0 | 0 | 3 |

## Gate Failures

No capability, reliability, or accuracy gate failures.

## Performance Warnings

| Suite | Scenario | Note |
|---|---|---|
| analytics-c2f1b4eb | groupby_time_1b_rows_wallclock | performance metrics are warning only |
| analytics-c2f1b4eb | columnar_compression_ratio | performance metrics are warning only |
