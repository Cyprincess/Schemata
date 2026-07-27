# Report snapshots

A persisted report has one `SchemataReportSnapshot` header and zero or more
`SchemataReportSnapshotChunk` rows. The writer stores each chunk independently, allowing a long
materialization to release each repository unit of work before the next chunk.

## Materialization lifecycle

`ReportSnapshotWriter<TReport, TSnapshot, TChunk>.WriteAsync` creates the header as `Pending`, moves
it to `Running`, then writes chunks of at most `SchemataReportOptions.ChunkSize` rows. It stores
`RowCount`, `ChunkCount`, `CapturedAt`, and `SnapshotState.Succeeded` after the materialized source
completes. `IReportSnapshotAdvisor` runs before that final header update.

| Outcome | Header state | Stored detail |
| --- | --- | --- |
| Successful materialization | `Succeeded` | `RowCount`, `ChunkCount`, `CapturedAt`, and schema. |
| Materialization exception | `Failed` | `Error` receives the exception message; written chunks remain available until retention removes them. |
| Cancellation observed at a chunk boundary | `Cancelled` | Completed row and chunk counts. |

`ReportSnapshotWriter` opens a fresh scope for header creation, each header update, and each chunk
write. `DefaultReportSnapshotStore<TSnapshot, TChunk>` opens scoped repositories for list, header,
chunk, and row-stream reads.

## Reading rows

`ReadSnapshotHandler<TSnapshot>` reads pages through `ReadSnapshotRequest` and returns
`ReadSnapshotResponse`.

| Property | Behavior |
| --- | --- |
| `ReadSnapshotRequest.PageSize` | Uses 1000 when absent, rejects zero or a negative value, and clamps values above `MaxReadPageSize`. |
| `ReadSnapshotRequest.PageToken` | Continues at the encoded chunk index and row offset. Invalid tokens raise `InvalidArgumentException`. |
| `ReadSnapshotResponse.Rows` | Rows decoded from only the chunks needed for the page. |
| `ReadSnapshotResponse.NextPageToken` | Carries the continuation location when further rows exist. |

The HTTP route is `GET /v1/{snapshotName}:read?page_size=&page_token=`. [AIP-158](https://google.aip.dev/158)
defines the terminal page with an empty `next_page_token`; clients stop paging when no continuation
token is present. The Report handler leaves `NextPageToken` unset after the final row.

## Retention

`ReportRetentionEnforcer<TSnapshot, TChunk>` runs on the write path after a successful snapshot. It
uses the resolved report's `ReportRetention` values:

| `ReportRetention` property | Victims |
| --- | --- |
| `MaxCount` | Successful snapshots beyond the newest retained count. |
| `MaxAgeDays` | Successful snapshots older than the age cutoff. |
| Neither value | Successful snapshots remain. |

Failed and cancelled snapshots become victims after
`SchemataReportOptions.IncompleteSnapshotGracePeriod`, which defaults to one day. The enforcer joins
the snapshot and chunk repositories in one unit of work for each victim, removes its chunks, and then
removes its header.

Retention applies to a named report with a `Retention` value. An inline persisted request has no
resolved report definition, so it does not contribute a retention policy.

## See also

- [Generation](generation.md) — selecting `Persist = true`
- [Transports](transports.md) — snapshot list, get, and `:read` endpoints
- [Scheduling](scheduling.md) — periodic snapshots
