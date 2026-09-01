# Report transports

`Schemata.Report.Http` and `Schemata.Report.Grpc` compose Report resources through the transport
features. `SchemataReportHttpFeature<TReport, TSnapshot, TChunk>` and
`SchemataReportGrpcFeature<TReport, TSnapshot, TChunk>` register `TReport` and `TSnapshot`; chunks
remain internal storage and receive no HTTP or gRPC resource registration.

```csharp
using Microsoft.AspNetCore.Builder;

builder.UseSchemata(schema => {
    schema.UseScheduling().MapHttp();

    var reports = schema.UseReport();
    reports.MapHttp().MapGrpc();
});
```

The host-level `MapHttp()` on Scheduling exposes operation polling. The Report-level `MapHttp()`
exposes report and snapshot resource methods.

## HTTP surface

The Report HTTP integration tests exercise these routes:

| Method | Route | Handler or resource behavior |
| --- | --- | --- |
| `POST` | `/v1/reports:generate` | Collection custom method using `GenerateReportRequest`; returns an operation. |
| `GET` | `/v1/reports/{report}/snapshots` | Lists persisted snapshot headers. |
| `GET` | `/v1/{snapshotName}:read?page_size=&page_token=` | Reads one page of snapshot rows. |
| `GET` | `/v1/{operationName}` | Polls the operation returned by generation. |

The snapshot list is a standard list endpoint. [AIP-132](https://google.aip.dev/132) specifies that
the list HTTP verb is `GET` and that paginated list requests include `page_size` and `page_token`.

`generate` and `read` are resource custom methods. [AIP-136](https://google.aip.dev/136) requires a
custom-method URI to use a colon followed by the custom verb and requires the verb to match the RPC
name. Its method-name guidance calls for a verb followed by a noun; the `GenerateReport` and
`ReadSnapshot` spelling below is Schemata's naming convention, rather than an AIP requirement for a
singular noun.

## gRPC services and methods

`GrpcResourceNaming.ServiceFullName` computes a service name as
`{Package ?? entityType.Namespace}.{Singular}Service`. Report entities have no `[ResourcePackage]`,
so their entity namespace supplies the package.

| Entity | gRPC service | Custom RPC |
| --- | --- | --- |
| `SchemataReport` | `Schemata.Report.Skeleton.ReportService` | `GenerateReport` |
| `SchemataReportSnapshot` | `Schemata.Report.Skeleton.SnapshotService` | `ReadSnapshot` |

`GrpcResourceNaming.CustomMethodName` constructs these custom method names from the resource method
verb and descriptor singular. The HTTP and gRPC features bind the same
`GenerateHandler<TReport, TSnapshot, TChunk>` and `ReadSnapshotHandler<TSnapshot>` implementations.

## Generation and operations

`GenerateReportRequest` accepts `Name` or `Query`, `Persist`, and `Sync`. Supplying both or neither
of `Name` and `Query` raises `InvalidArgumentException`. A synchronous request dispatches
`RunReportRequest` and creates a terminal operation. An asynchronous request is dispatched to
`GenerateHandler<TReport, TSnapshot, TChunk>`, which triggers
`ReportGenerationJob<TReport, TSnapshot, TChunk>` and returns a pending operation.

The operation name returned by generation is suitable for the polling route above. [AIP-151](https://google.aip.dev/151)
requires operations to use the shared `google.longrunning.Operation` type and shared Operations
service rather than a service-specific operation interface.

## See also

- [Generation](generation.md) — request semantics and operation prerequisites
- [Snapshots](snapshots.md) — snapshot paging behavior
- [Overview](overview.md) — feature activation and priorities
