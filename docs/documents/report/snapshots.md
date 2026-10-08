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

The application assigns missing `Name` values through `IRepositoryAddAdvisor<TSnapshot>` and
`IRepositoryAddAdvisor<TChunk>`, registered with `TryAddEnumerable` before
`AdviceAddCanonicalName.DefaultOrder` (120,000,000). Preserve explicitly supplied names. The writer
adds and commits the header before reading its assigned name into chunk parent references or its
canonical name into the result. Each chunk runs its own repository add pipeline; its `Index` is
ordering data, not a generated resource name. Missing naming policy fails through the normal
canonical-name validation path rather than a framework fallback.

The header's `Operation`, when supplied, is the persisted operation's canonical name. Consumers
should retain stored names and references when migrating existing data, independently of any UID
or chunk-index convention their earlier naming policy used.

Implementation: `src/Schemata.Report.Foundation/Snapshots/ReportSnapshotWriter.cs`.

Computed and expression fields publish `Dynamic` descriptors before the first independently committed
chunk. Those cells persist Common's typed kind/value payload recursively, so later chunks can contain
different admitted scalar kinds without rewriting earlier chunks or guessing JSON number/string types.
Known uniform fields retain schema-directed JSON. The page handler refreshes the same-UID header after
bounded chunk reads, then restores typed Dynamic values. HTTP publishes the same typed payload and
gRPC publishes scalar slots with optional kind labels; decimal, UInt64, bytes and temporal values retain
their exact canonical representation. `ReadSnapshotResponse` exposes restored CLR scalar values locally.


## Reading rows

Resolve `IReportSnapshotStore` for local reads. `ListAsync` accepts the owning report's canonical
name (`reports/A`); `GetAsync`, `GetChunkAsync`, and `ReadRowsAsync` accept the complete snapshot
name (`reports/A/snapshots/daily`). The default store parses these targets with
`ResourceNameDescriptor`. Malformed names raise `InvalidArgumentException` with the existing
`INVALID_NAME` reason.

Snapshot headers persist the report leaf in `Report`. Chunks persist that report leaf in `Report`
and the snapshot leaf in `Snapshot`; the writer reads both from the committed header. Reads scope
the persisted relation with both fields. `reports/A/snapshots/daily` and
`reports/B/snapshots/daily` can therefore share the leaf `daily` while returning their own headers,
chunks, streams, and pages. A snapshot leaf alone is not an addressable target.

Implementation: `src/Schemata.Report.Foundation/Snapshots/DefaultReportSnapshotStore.cs`.

`ReadSnapshotHandler<TSnapshot>` reads pages through `ReadSnapshotRequest` and returns
`ReadSnapshotResponse`.

| Property | Behavior |
| --- | --- |
| `ReadSnapshotRequest.PageSize` | Uses 1000 when absent, rejects zero or a negative value, and clamps values above `MaxReadPageSize`. |
| `ReadSnapshotRequest.PageToken` | Protected continuation bound to the complete snapshot name, normalized page size, trusted tenant UID, and caller identity. Invalid tokens raise `InvalidArgumentException` before snapshot-store reads. |
| `ReadSnapshotResponse.Rows` | Rows decoded from only the chunks needed for the page. |
| `ReadSnapshotResponse.NextPageToken` | Carries the continuation location when further rows exist. |

The HTTP route is `GET /v1/{snapshotName}:read?page_size=&page_token=`. [AIP-158](https://google.aip.dev/158)
defines the terminal page with an empty `next_page_token`; clients stop paging when no continuation
token is present. The Report handler leaves `NextPageToken` unset after the final row.

The handler takes the caller from `ReadSnapshotRequest.Principal` and the tenant from
`TenantContext.Current.Uid`. `ProtectedContinuationCaller` in `src/Schemata.Common/` captures the
principal's default identity, authentication state and authentication type. An authenticated identity
uses its nonblank name-identifier claim, then `sub`, then `Identity.Name`; the name fallback also binds
its name-claim type. Anonymous identities share an anonymous binding regardless of their claims.
Local dispatchers supply the principal on the request; HTTP and gRPC adapters forward their trusted
server principal. Continue with the same effective page size, target, tenant, and caller binding.
Page sizes that clamp to the same configured maximum share that effective size. An authenticated
identity with no usable identifier or name can read a terminal page, but creating or resuming a
continuation raises `InvalidArgumentException` with reason `INVALID_PAGE_TOKEN`.

Report installs ASP.NET DataProtection through `AddSchemataReport`. Configure a persistent key ring
and application discriminator on the host's `AddDataProtection()` builder to continue across host
restarts or instances. Tokens from another key ring, altered or truncated tokens, negative positions,
and unprotected offsets are rejected. Continuation arithmetic rejects overflow.

`Schemata.Common.ProtectedContinuation` streams the internal JSON payload through Brotli and protects
the compressed bytes before Base64 URL encoding. It introduces no token-size or decompressed-size
policy; consumers retain their input-size policy at the transport boundary.

Implementation: `src/Schemata.Report.Foundation/Handlers/ReadSnapshotHandler.cs`,
`src/Schemata.Report.Foundation/Handlers/ReportReadPageToken.cs`, and
`src/Schemata.Common/ProtectedContinuation.cs`.

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
the snapshot and chunk repositories in one unit of work for each victim. Its chunk query matches
both the victim's `Report` and `Name` against each chunk's `Report` and `Snapshot`. It passes that
same outer transaction to every chunk and header resource mutation, then commits once. A failed
delete rolls back that victim's deletions; a same-named snapshot under another report remains.

Retention applies to a named report with a `Retention` value. An inline persisted request has no
resolved report definition, so it does not contribute a retention policy.

## See also

- [Generation](generation.md) — selecting `Persist = true`
- [Transports](transports.md) — snapshot list, get, and `:read` endpoints
- [Scheduling](scheduling.md) — periodic snapshots
