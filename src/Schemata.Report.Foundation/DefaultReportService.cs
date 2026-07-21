using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Advice;
using Schemata.Common;
using Schemata.Insight.Foundation;
using Schemata.Insight.Skeleton;
using Schemata.Report.Foundation.Internal;
using Schemata.Report.Skeleton;
using Schemata.Scheduling.Skeleton;

namespace Schemata.Report.Foundation;

/// <summary>Default report service for inline materialization, snapshots, and scheduler-backed generation.</summary>
/// <typeparam name="TReport">Persisted report-definition entity type.</typeparam>
/// <typeparam name="TSnapshot">Persisted snapshot-header entity type.</typeparam>
/// <typeparam name="TChunk">Persisted snapshot-chunk entity type.</typeparam>
public sealed class DefaultReportService<TReport, TSnapshot, TChunk> : IReportService
    where TReport : SchemataReport, new()
    where TSnapshot : SchemataReportSnapshot, new()
    where TChunk : SchemataReportSnapshotChunk, new()
{
    private readonly IReportDefinitionStore                           _definitions;
    private readonly IReportMaterializer                              _materializer;
    private readonly SchemataReportOptions                            _options;
    private readonly ReportExecutionContext                           _execution;
    private readonly InsightPlanBuilder                               _plans;
    private readonly IServiceProvider                                 _services;
    private readonly ReportSnapshotWriter<TReport, TSnapshot, TChunk> _writer;

    /// <summary>Creates the default report service.</summary>
    /// <param name="services">Service provider resolving advisors and the optional scheduler.</param>
    /// <param name="definitions">Composite report-definition store.</param>
    /// <param name="plans">Insight plan builder for resolved report queries.</param>
    /// <param name="materializer">Single-pass plan materializer.</param>
    /// <param name="writer">Bounded persisted snapshot writer.</param>
    /// <param name="options">Inline and chunk limits.</param>
    public DefaultReportService(
        IServiceProvider                                  services,
        IReportDefinitionStore                            definitions,
        InsightPlanBuilder                                plans,
        IReportMaterializer                               materializer,
        ReportSnapshotWriter<TReport, TSnapshot, TChunk> writer,
        IOptions<SchemataReportOptions>                  options
    ) {
        _services     = services;
        _definitions  = definitions;
        _plans        = plans;
        _materializer = materializer;
        _writer       = writer;
        _execution    = services.GetService<ReportExecutionContext>() ?? new();
        _options      = options.Value;
    }

    /// <inheritdoc />
    public async ValueTask<ReportResult> RunAsync(
        ReportRequest     request,
        ClaimsPrincipal?  principal = null,
        CancellationToken ct        = default
    ) {
        ArgumentNullException.ThrowIfNull(request);
        var generation = new ReportGenerateContext(request, request.Name, _execution.Kind, principal);
        var advice = new AdviceContext(_services);
        await Advisor.For<IReportGenerateAdvisor>().RunAsync(advice, generation, ct);

        var (report, query) = await ResolveAsync(generation.Request, ct);
        var definitionContext = new ReportDefinitionContext(query, report);
        await Advisor.For<IReportDefinitionAdvisor>().RunAsync(advice, definitionContext, ct);
        query = definitionContext.Query;

        var plan = await _plans.BuildAsync(query, ct);
        if (!generation.Request.Persist) {
            return await CollectInlineAsync(plan, query, generation.Principal, ct);
        }

        return await _writer.WriteAsync(
                   report,
                   _execution.Kind,
                   token => _materializer.MaterializeAsync(plan, query, generation.Principal, token),
                   _execution.Operation,
                   _execution.IsCancelled,
                   ct);
    }

    /// <inheritdoc />
    public async ValueTask<Operation> GenerateAsync(
        ReportRequest     request,
        CancellationToken ct = default
    ) {
        ArgumentNullException.ThrowIfNull(request);
        var scheduler = _services.GetService<IScheduler>()
                        ?? throw new FailedPreconditionException(message: "Report generation requires a scheduler.");
        var uid = Identifiers.NewUid();
        var context = new JobContext {
            ExecutionUid = uid,
            Method       = "generate",
            ArgsJson     = JsonSerializer.Serialize(request, SchemataJson.Default),
        };
        var execution = await scheduler.TriggerAsync<ReportGenerationJob<TReport, TSnapshot, TChunk>>(context, ct);
        return OperationMapper.FromExecution(execution);
    }

    private async ValueTask<ReportResult> CollectInlineAsync(
        PlanNode            plan,
        QueryInsightRequest query,
        ClaimsPrincipal?    principal,
        CancellationToken   ct
    ) {
        await using var materialized = await _materializer.MaterializeAsync(plan, query, principal, ct);
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
            var definition = await _definitions.ResolveAsync(request.Name, ct);
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
