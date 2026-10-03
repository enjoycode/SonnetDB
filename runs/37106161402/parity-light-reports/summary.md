# SonnetDB Parity Summary

| Field | Value |
|---|---|
| Profile | light |
| Status | passing |
| Pass rate | 100% |
| Scenarios | 24 passed / 27 skipped / 0 failed / 51 total |
| Warning-only performance scenarios | 2 |
| Commit | 46dd3a5ec4a7d2e3ae7b07b139289f34c767345f |
| GitHub run | 37106161402 |

## Suites

| Suite | Passed | Skipped | Failed | Total |
|---|---:|---:|---:|---:|
| analytics-976cbf48 | 0 | 5 | 0 | 5 |
| document-f6f6336a | 0 | 5 | 0 | 5 |
| fulltext-da1de011 | 0 | 6 | 0 | 6 |
| graph-0fc080fe | 1 | 0 | 0 | 1 |
| kv-a18087f9 | 5 | 0 | 0 | 5 |
| mq-321d42c8 | 5 | 0 | 0 | 5 |
| object-eb175cc8 | 5 | 0 | 0 | 5 |
| relational-dce3b733 | 8 | 1 | 0 | 9 |
| tsdb-cec86c47 | 0 | 7 | 0 | 7 |
| vector-7c468a39 | 0 | 3 | 0 | 3 |

## Gate Failures

No capability, reliability, or accuracy gate failures.

## Performance Warnings

| Suite | Scenario | Note |
|---|---|---|
| analytics-976cbf48 | groupby_time_1b_rows_wallclock | performance metrics are warning only |
| analytics-976cbf48 | columnar_compression_ratio | performance metrics are warning only |
