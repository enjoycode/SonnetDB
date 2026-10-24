# SonnetDB Parity Summary

| Field | Value |
|---|---|
| Profile | light |
| Status | passing |
| Pass rate | 100% |
| Scenarios | 24 passed / 27 skipped / 0 failed / 51 total |
| Warning-only performance scenarios | 2 |
| Commit | 46dd3a5ec4a7d2e3ae7b07b139289f34c767345f |
| GitHub run | 40071325860 |

## Suites

| Suite | Passed | Skipped | Failed | Total |
|---|---:|---:|---:|---:|
| analytics-f0246e1e | 0 | 5 | 0 | 5 |
| document-e37eac27 | 0 | 5 | 0 | 5 |
| fulltext-08b44770 | 0 | 6 | 0 | 6 |
| graph-e7b00400 | 1 | 0 | 0 | 1 |
| kv-dafdab29 | 5 | 0 | 0 | 5 |
| mq-9a2f0119 | 5 | 0 | 0 | 5 |
| object-3e38d5a0 | 5 | 0 | 0 | 5 |
| relational-81eee32e | 8 | 1 | 0 | 9 |
| tsdb-31e32d8d | 0 | 7 | 0 | 7 |
| vector-3ce5d4e8 | 0 | 3 | 0 | 3 |

## Gate Failures

No capability, reliability, or accuracy gate failures.

## Performance Warnings

| Suite | Scenario | Note |
|---|---|---|
| analytics-f0246e1e | groupby_time_1b_rows_wallclock | performance metrics are warning only |
| analytics-f0246e1e | columnar_compression_ratio | performance metrics are warning only |
