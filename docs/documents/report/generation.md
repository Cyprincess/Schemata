# Report generation

`IReportService` dispatches a named definition or an inline Insight query. `ReportRequest` requires
exactly one of `Name` and `Query`; `Persist` chooses whether rows remain in the response or become a snapshot.

```csharp
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Resource;
using Schemata.Report.Skeleton;

public interface IReportService
{
    ValueTask<ReportResult> RunAsync(
        ReportRequest request,
        ClaimsPrincipal? principal = null,
        CancellationToken ct = default);

    ValueTask<Operation> GenerateAsync(
        ReportRequest request,
        CancellationToken ct = default);
}
```

## `RunAsync`

`RunAsync` dispatches `RunReportRequest`. `RunReportHandler<TReport, TSnapshot, TChunk>` invokes
`IReportGenerateAdvisor`, resolves the named or inline definition, invokes `IReportDefinitionAdvisor`,
builds an Insight plan, and materializes it under the supplied principal. Insight applies its source
access and entitlement providers during that materialization.

| Request shape | Result |
| --- | --- |
| `Name` or `Query`, `Persist = false` | `ReportResult.Response` contains inline rows and schema. `MaxInlineRows` bounds collection. |
| `Name` or `Query`, `Persist = true` | `ReportSnapshotWriter<TReport, TSnapshot, TChunk>` returns a snapshot canonical name in `ReportResult.Snapshot`. |

An inline result that reaches `MaxInlineRows` throws `ReportException` with reason
`INLINE_ROW_LIMIT` and asks the caller to rerun with `Persist=true`.

`ReportGenerateContext.Principal` begins as the caller principal. Dispatched and scheduled jobs pass
`null`; an `IReportGenerateAdvisor` can replace that principal before Insight execution.

## `GenerateAsync` and operations

`GenerateAsync` dispatches `GenerateReportRequest`. For asynchronous generation,
`GenerateHandler<TReport, TSnapshot, TChunk>` resolves `IScheduler`, serializes the `ReportRequest`
into a `JobContext`, triggers `ReportGenerationJob<TReport, TSnapshot, TChunk>`, and returns the
pending `Operation`. The job writes the eventual snapshot reference or inline response.

[AIP-151](https://google.aip.dev/151) directs methods that may take significant time to return a
`google.longrunning.Operation` and use the shared Operations service. Report follows that operation
model through the Scheduling domain.

`ReportGenerationJob<TReport, TSnapshot, TChunk>` uses these run kinds:

| Input to the job | `ReportRunKind` | Persistence behavior |
| --- | --- | --- |
| `JobContext.ArgsJson` from `GenerateHandler` | `ImmediatePersisted` | Replays the request's `Persist` value. |
| `JobContext.Variables["report"]` from a periodic schedule | `Scheduled` | Generates a named report with `Persist = true`. |

## Transport request

`GenerateHandler<TReport, TSnapshot, TChunk>` implements the collection-scoped `generate` method
with `GenerateReportRequest`:

| Property | Meaning |
| --- | --- |
| `Name` | Named definition; mutually exclusive with `Query`. |
| `Query` | Inline `QueryInsightRequest`; mutually exclusive with `Name`. |
| `Persist` | Writes a snapshot when `true`. |
| `Sync` | Runs immediately and returns a terminal `Operation` when `true`; otherwise dispatches a pending operation. |

The handler resolves `IOperationService` before either sync or asynchronous generation. An absent
operation service raises `FailedPreconditionException`, which the HTTP transport returns as 412. See
the [Error Model](../core/error-model.md) for the full exception and status table, including where
Schemata's HTTP codes diverge from `google.rpc.Code`.

The asynchronous handler path also needs `IScheduler`. Enable the host Scheduling feature, then add
the Report Scheduling bridge when periodic reports are required:

```csharp
using Microsoft.AspNetCore.Builder;

builder.UseSchemata(schema => {
    schema.UseScheduling();

    var reports = schema.UseReport();
    reports.UseScheduling();
});
```

## See also

- [Definitions](definitions.md) — resolving `Name` and `Query`
- [Snapshots](snapshots.md) — persisted materialization and reads
- [Transports](transports.md) — `POST /v1/reports:generate` and operation polling
- [Scheduling](scheduling.md) — periodic report jobs
