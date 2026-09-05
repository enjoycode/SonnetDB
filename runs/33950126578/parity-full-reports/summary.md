# SonnetDB Parity Summary

| Field | Value |
|---|---|
| Profile | full |
| Status | passing |
| Pass rate | 100% |
| Scenarios | 44 passed / 7 skipped / 0 failed / 51 total |
| Warning-only performance scenarios | 2 |
| Commit | f2cad5af8b33a3473ada3d3e7047f75a1e8f5f5f |
| GitHub run | 33950126578 |

## Suites

| Suite | Passed | Skipped | Failed | Total |
|---|---:|---:|---:|---:|
| analytics-67ba40c8 | 0 | 5 | 0 | 5 |
| document-b6af6654 | 5 | 0 | 0 | 5 |
| fulltext-954edf05 | 6 | 0 | 0 | 6 |
| graph-08a4eccf | 1 | 0 | 0 | 1 |
| kv-9acb0cc9 | 5 | 0 | 0 | 5 |
| mq-50cf8850 | 5 | 0 | 0 | 5 |
| object-9fe38324 | 5 | 0 | 0 | 5 |
| relational-61b99edc | 8 | 1 | 0 | 9 |
| tsdb-bfa906ee | 6 | 1 | 0 | 7 |
| vector-6c2e2bfd | 3 | 0 | 0 | 3 |

## Gate Failures

No capability, reliability, or accuracy gate failures.

## Performance Warnings

| Suite | Scenario | Note |
|---|---|---|
| analytics-67ba40c8 | groupby_time_1b_rows_wallclock | performance metrics are warning only |
| analytics-67ba40c8 | columnar_compression_ratio | performance metrics are warning only |
