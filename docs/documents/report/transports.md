# Report transports

`Schemata.Report.Http` and `Schemata.Report.Grpc` expose Report resources through their domain transport features. `MapHttp()` and `MapGrpc()` activate one feature each; shared Resource transport behavior is supplied through feature dependencies. Report, snapshot, and method envelopes receive dispatcher-wrap security when the Report builder enables it.

```csharp
using Microsoft.AspNetCore.Builder;

builder.UseSchemata(schema => {
    schema.UseSecurity();
    schema.UseScheduling().MapHttp();

    schema.UseReport()
          .WithAuthentication("Bearer")
          .WithAuthorization()
          .MapHttp()
          .MapGrpc();
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
| `SchemataReport` | `Schemata.Report.Skeleton.Entities.ReportService` | `GenerateReport` |
| `SchemataReportSnapshot` | `Schemata.Report.Skeleton.Entities.SnapshotService` | `ReadSnapshot` |

`GrpcResourceNaming.CustomMethodName` constructs these custom method names from the resource method
verb and descriptor singular. The HTTP and gRPC features bind the same
`GenerateHandler<TReport, TSnapshot, TChunk>` and `ReadSnapshotHandler<TSnapshot>` implementations.

Report CRUD request advisors reject a conflicting entity triple before resource reads or writes.
Snapshot `:read` checks the same selection before loading its header. Raw generation commands are
guarded in the dispatcher pipeline before handler construction. Both transports consume the same
Report registration result and report `FAILED_PRECONDITION` for a conflicting selection.

Implementation: `src/Schemata.Report.Foundation/Advisors/ReportEntityRequestAdvisor.cs`,
`ReportEntityCrudRequestAdvisor.cs`, `ReportEntityMethodRequestAdvisor.cs`, and
`ReportCommandPipelineAdvisor.cs`.

### Snapshot page wire

`ReadSnapshotHandler<TSnapshot>` returns the protobuf-free `ReadSnapshotResponse`: one page of
dictionary rows, its continuation token, and the schema read from the persisted snapshot header.
`ReportSnapshotWriter` stores the opened schema before publishing chunks. It completes computed
field labels from each bounded chunk through `SchemaBuilder.Complete`, and commits each schema
refinement before that chunk. Running snapshots expose rows with the durable restoration labels
already in their header. A later value incompatible with an established label fails before its
chunk is published; null-first fields may acquire their label when a later non-null value arrives.
Snapshot page reads refresh the header after loading their bounded rows, so a concurrent schema
refinement covers any newly published chunk included in that page. The refreshed header must
identify the same snapshot UID; deletion or reconstruction during the read rejects the page.
This ordering does not establish a database-wide snapshot transaction.

`ReportGrpcModelContributor` configures a `ReadSnapshotGrpcResponse` surrogate on the resource
runtime model before methods are bound. Its fields are `Rows` (1), `NextPageToken` (2), and
`Schema` (3). Rows use the shared `DynamicStruct`, `DynamicValue`, and `DynamicList` messages;
schema uses `DynamicFieldDescriptor<FieldType>`. The adapter decodes persisted JSON with those
labels before shared value encoding, retaining exact ulong and decimal values, nulls, empty
containers, temporal formats, and nested element types. Resource marshalling and reflection
schema generation use that same configured model.

Sources: `src/Schemata.Report.Grpc/ReadSnapshotGrpcResponse.cs`, `ReportGrpcModelContributor.cs`,
`src/Schemata.Report.Foundation/Handlers/ReadSnapshotHandler.cs`, and
`src/Schemata.Report.Foundation/Snapshots/ReportSnapshotWriter.cs`.

## Generation and operations

`GenerateReportRequest` accepts `Name` or `Query`, `Persist`, and `Sync`. Supplying both or neither
of `Name` and `Query` raises `InvalidArgumentException`. A synchronous request uses
`IOperationService.ExecuteAsync` to persist the operation before dispatching `RunReportRequest`
inside its callback, then updates that same row with the terminal outcome. An asynchronous request
triggers `ReportGenerationJob<TReport, TSnapshot, TChunk>` and returns the persisted pending
operation. Both paths return the actual stored canonical name; a caller-reserved execution UID
does not predict the polling URI. The host must supply execution naming through a repository add
advisor before canonical-name derivation.

Implementation: `src/Schemata.Report.Foundation/Handlers/GenerateHandler.cs` and
`src/Schemata.Scheduling.Skeleton/OperationMapper.cs`.

The operation name returned by generation is suitable for the polling route above. [AIP-151](https://google.aip.dev/151)
requires operations to use the shared `google.longrunning.Operation` type and shared Operations
service rather than a service-specific operation interface.

## See also

- [Generation](generation.md) — request semantics and operation prerequisites
- [Snapshots](snapshots.md) — snapshot paging behavior
- [Overview](overview.md) — feature activation and priorities
