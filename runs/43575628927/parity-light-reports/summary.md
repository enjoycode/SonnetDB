# SonnetDB Parity Summary

| Field | Value |
|---|---|
| Profile | light |
| Status | passing |
| Pass rate | 100% |
| Scenarios | 24 passed / 27 skipped / 0 failed / 51 total |
| Warning-only performance scenarios | 2 |
| Commit | 46dd3a5ec4a7d2e3ae7b07b139289f34c767345f |
| GitHub run | 43575628927 |

## Suites

| Suite | Passed | Skipped | Failed | Total |
|---|---:|---:|---:|---:|
| analytics-45911cac | 0 | 5 | 0 | 5 |
| document-6034087b | 0 | 5 | 0 | 5 |
| fulltext-09ff03e1 | 0 | 6 | 0 | 6 |
| graph-d2216914 | 1 | 0 | 0 | 1 |
| kv-5ba1f804 | 5 | 0 | 0 | 5 |
| mq-b7839d37 | 5 | 0 | 0 | 5 |
| object-ef37be3d | 5 | 0 | 0 | 5 |
| relational-cc9a4554 | 8 | 1 | 0 | 9 |
| tsdb-e727b8ed | 0 | 7 | 0 | 7 |
| vector-3d4addb3 | 0 | 3 | 0 | 3 |

## Gate Failures

No capability, reliability, or accuracy gate failures.

## Performance Warnings

| Suite | Scenario | Note |
|---|---|---|
| analytics-45911cac | groupby_time_1b_rows_wallclock | performance metrics are warning only |
| analytics-45911cac | columnar_compression_ratio | performance metrics are warning only |
