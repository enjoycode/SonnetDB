# SonnetDB Parity Summary

| Field | Value |
|---|---|
| Profile | light |
| Status | passing |
| Pass rate | 100% |
| Scenarios | 24 passed / 27 skipped / 0 failed / 51 total |
| Warning-only performance scenarios | 2 |
| Commit | 46dd3a5ec4a7d2e3ae7b07b139289f34c767345f |
| GitHub run | 44887476123 |

## Suites

| Suite | Passed | Skipped | Failed | Total |
|---|---:|---:|---:|---:|
| analytics-2162b391 | 0 | 5 | 0 | 5 |
| document-2e3705dd | 0 | 5 | 0 | 5 |
| fulltext-a28211a7 | 0 | 6 | 0 | 6 |
| graph-45229106 | 1 | 0 | 0 | 1 |
| kv-f07adb98 | 5 | 0 | 0 | 5 |
| mq-91bd9976 | 5 | 0 | 0 | 5 |
| object-6da7afa4 | 5 | 0 | 0 | 5 |
| relational-6d5b8594 | 8 | 1 | 0 | 9 |
| tsdb-62285533 | 0 | 7 | 0 | 7 |
| vector-86d7c7da | 0 | 3 | 0 | 3 |

## Gate Failures

No capability, reliability, or accuracy gate failures.

## Performance Warnings

| Suite | Scenario | Note |
|---|---|---|
| analytics-2162b391 | groupby_time_1b_rows_wallclock | performance metrics are warning only |
| analytics-2162b391 | columnar_compression_ratio | performance metrics are warning only |
