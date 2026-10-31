# SonnetDB Parity Summary

| Field | Value |
|---|---|
| Profile | light |
| Status | passing |
| Pass rate | 100% |
| Scenarios | 24 passed / 27 skipped / 0 failed / 51 total |
| Warning-only performance scenarios | 2 |
| Commit | 46dd3a5ec4a7d2e3ae7b07b139289f34c767345f |
| GitHub run | 41182118364 |

## Suites

| Suite | Passed | Skipped | Failed | Total |
|---|---:|---:|---:|---:|
| analytics-303a25a6 | 0 | 5 | 0 | 5 |
| document-a6017027 | 0 | 5 | 0 | 5 |
| fulltext-bc8bad7a | 0 | 6 | 0 | 6 |
| graph-03e59fe8 | 1 | 0 | 0 | 1 |
| kv-cea9c640 | 5 | 0 | 0 | 5 |
| mq-3ac6fe63 | 5 | 0 | 0 | 5 |
| object-536dc60e | 5 | 0 | 0 | 5 |
| relational-d5f22728 | 8 | 1 | 0 | 9 |
| tsdb-6cb8c032 | 0 | 7 | 0 | 7 |
| vector-6c706732 | 0 | 3 | 0 | 3 |

## Gate Failures

No capability, reliability, or accuracy gate failures.

## Performance Warnings

| Suite | Scenario | Note |
|---|---|---|
| analytics-303a25a6 | groupby_time_1b_rows_wallclock | performance metrics are warning only |
| analytics-303a25a6 | columnar_compression_ratio | performance metrics are warning only |
