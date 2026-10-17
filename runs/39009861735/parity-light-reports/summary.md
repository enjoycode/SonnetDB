# SonnetDB Parity Summary

| Field | Value |
|---|---|
| Profile | light |
| Status | passing |
| Pass rate | 100% |
| Scenarios | 24 passed / 27 skipped / 0 failed / 51 total |
| Warning-only performance scenarios | 2 |
| Commit | 46dd3a5ec4a7d2e3ae7b07b139289f34c767345f |
| GitHub run | 39009861735 |

## Suites

| Suite | Passed | Skipped | Failed | Total |
|---|---:|---:|---:|---:|
| analytics-0fa68fda | 0 | 5 | 0 | 5 |
| document-27bc49be | 0 | 5 | 0 | 5 |
| fulltext-0f854fb8 | 0 | 6 | 0 | 6 |
| graph-13005730 | 1 | 0 | 0 | 1 |
| kv-cb763b32 | 5 | 0 | 0 | 5 |
| mq-83406bf9 | 5 | 0 | 0 | 5 |
| object-05290bb3 | 5 | 0 | 0 | 5 |
| relational-16a6dead | 8 | 1 | 0 | 9 |
| tsdb-8e04e3bd | 0 | 7 | 0 | 7 |
| vector-1e465e75 | 0 | 3 | 0 | 3 |

## Gate Failures

No capability, reliability, or accuracy gate failures.

## Performance Warnings

| Suite | Scenario | Note |
|---|---|---|
| analytics-0fa68fda | groupby_time_1b_rows_wallclock | performance metrics are warning only |
| analytics-0fa68fda | columnar_compression_ratio | performance metrics are warning only |
