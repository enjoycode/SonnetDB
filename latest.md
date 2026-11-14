# SonnetDB Parity Summary

| Field | Value |
|---|---|
| Profile | full |
| Status | passing |
| Pass rate | 100% |
| Scenarios | 49 passed / 2 skipped / 0 failed / 51 total |
| Warning-only performance scenarios | 2 |
| Commit | 46dd3a5ec4a7d2e3ae7b07b139289f34c767345f |
| GitHub run | 43575628927 |

## Suites

| Suite | Passed | Skipped | Failed | Total |
|---|---:|---:|---:|---:|
| analytics-797c150a | 5 | 0 | 0 | 5 |
| document-81ba3fb8 | 5 | 0 | 0 | 5 |
| fulltext-02581064 | 6 | 0 | 0 | 6 |
| graph-e536ef9a | 1 | 0 | 0 | 1 |
| kv-15a1745e | 5 | 0 | 0 | 5 |
| mq-121f1fa6 | 5 | 0 | 0 | 5 |
| object-5f554d9d | 5 | 0 | 0 | 5 |
| relational-84e9f1ef | 8 | 1 | 0 | 9 |
| tsdb-ca088d6d | 6 | 1 | 0 | 7 |
| vector-a5eca939 | 3 | 0 | 0 | 3 |

## Gate Failures

No capability, reliability, or accuracy gate failures.

## Performance Warnings

| Suite | Scenario | Note |
|---|---|---|
| analytics-797c150a | groupby_time_1b_rows_wallclock | performance metrics are warning only |
| analytics-797c150a | columnar_compression_ratio | performance metrics are warning only |
