# SonnetDB Parity Summary

| Field | Value |
|---|---|
| Profile | light |
| Status | passing |
| Pass rate | 100% |
| Scenarios | 24 passed / 27 skipped / 0 failed / 51 total |
| Warning-only performance scenarios | 2 |
| Commit | 46dd3a5ec4a7d2e3ae7b07b139289f34c767345f |
| GitHub run | 38036194252 |

## Suites

| Suite | Passed | Skipped | Failed | Total |
|---|---:|---:|---:|---:|
| analytics-83584736 | 0 | 5 | 0 | 5 |
| document-2b7a976f | 0 | 5 | 0 | 5 |
| fulltext-098a2561 | 0 | 6 | 0 | 6 |
| graph-c7f132b7 | 1 | 0 | 0 | 1 |
| kv-75be3bba | 5 | 0 | 0 | 5 |
| mq-757d66b1 | 5 | 0 | 0 | 5 |
| object-570fe220 | 5 | 0 | 0 | 5 |
| relational-d0499bb8 | 8 | 1 | 0 | 9 |
| tsdb-6b429e22 | 0 | 7 | 0 | 7 |
| vector-33cec128 | 0 | 3 | 0 | 3 |

## Gate Failures

No capability, reliability, or accuracy gate failures.

## Performance Warnings

| Suite | Scenario | Note |
|---|---|---|
| analytics-83584736 | groupby_time_1b_rows_wallclock | performance metrics are warning only |
| analytics-83584736 | columnar_compression_ratio | performance metrics are warning only |
