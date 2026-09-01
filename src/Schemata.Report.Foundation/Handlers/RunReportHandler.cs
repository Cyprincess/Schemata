using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Advice;
using Schemata.Insight.Foundation;
using Schemata.Insight.Skeleton;
using Schemata.Messaging.Skeleton;
using Schemata.Report.Foundation.Commands;
using Schemata.Report.Foundation.Internal;
using Schemata.Report.Skeleton;

namespace Schemata.Report.Foundation;

/// <summary>Runs the advisor-gated Report definition, planning, materialization, and snapshot pipeline.</summary>
public sealed class RunReportHandler<TReport, TSnapshot, TChunk>(
    IReportDefinitionStore                            definitions,
    InsightPlanBuilder                                plans,
    PlanExecutor                                      executor,
    ReportSnapshotWriter<TReport, TSnapshot, TChunk> writer,
    ReportExecutionContext                            execution,
    IOptions<SchemataReportOptions>                   options
) : IRequestHandler<RunReportRequest, ReportResult>
    where TReport : SchemataReport, new()
    where TSnapshot : SchemataReportSnapshot, new()
    where TChunk : SchemataReportSnapshotChunk, new()
{
    private readonly SchemataReportOptions _options = options.Value;

    public async Task<ReportResult> HandleAsync(
        RunReportRequest  request,
        CancellationToken ct = default
    ) {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Request);
        var ctx = AdviceContext.Require();
        var generation = new ReportGenerateContext(
            request.Request, request.Request.Name, execution.Kind, request.Principal);
        await Advisor.For<IReportGenerateAdvisor>().RunAsync(ctx, generation, ct);

        var (report, query) = await ResolveAsync(generation.Request, ct);
        var definitionContext = new ReportDefinitionContext(query, report);
        await Advisor.For<IReportDefinitionAdvisor>().RunAsync(ctx, definitionContext, ct);
        query = definitionContext.Query;

        var plan = await plans.BuildAsync(query, ct);
        if (!generation.Request.Persist) {
            return await CollectInlineAsync(plan, query, generation.Principal, ct);
        }

        return await writer.WriteAsync(
                   report,
                   execution.Kind,
                   token => executor.MaterializeAsync(plan, query, generation.Principal, ct: token),
                   execution.Operation,
                   execution.IsCancelled,
                   ct);
    }

    private async ValueTask<ReportResult> CollectInlineAsync(
        PlanNode            plan,
        QueryInsightRequest query,
        ClaimsPrincipal?    principal,
        CancellationToken   ct
    ) {
        await using var materialized = await executor.MaterializeAsync(plan, query, principal, ct: ct);
        var response = new QueryInsightResponse { Schema = materialized.Schema };
        await foreach (var row in materialized.Rows.WithCancellation(ct)) {
            if (response.Rows.Count >= _options.MaxInlineRows) {
                throw new ReportException("INLINE_ROW_LIMIT", "Report exceeds MaxInlineRows; rerun with Persist=true.");
            }

            response.Rows.Add(row);
        }

        response.TotalSize = response.Rows.Count;
        return new() { Response = response };
    }

    private async ValueTask<(SchemataReport? Report, QueryInsightRequest Query)> ResolveAsync(
        ReportRequest     request,
        CancellationToken ct
    ) {
        if (!string.IsNullOrWhiteSpace(request.Name) && request.Query is null) {
            var definition = await definitions.ResolveAsync(request.Name, ct);
            return definition is not null
                ? definition.Value
                : throw new NotFoundException(message: $"Report '{request.Name}' was not found.");
        }

        if (string.IsNullOrWhiteSpace(request.Name) && request.Query is not null) {
            return (null, request.Query);
        }

        throw new InvalidArgumentException(message: "Specify exactly one report name or inline query.");
    }
}
