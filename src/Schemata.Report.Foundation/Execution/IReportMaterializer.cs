using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Insight.Foundation;
using Schemata.Insight.Skeleton;

namespace Schemata.Report.Foundation;

/// <summary>Opens the single-pass row stream used to materialize a report plan.</summary>
public interface IReportMaterializer
{
    /// <summary>Opens an unpaged report row stream.</summary>
    /// <param name="plan">The report's insight plan.</param>
    /// <param name="request">The source query request.</param>
    /// <param name="principal">The principal the source drivers authorize against.</param>
    /// <param name="ct">Cancellation token for opening the stream.</param>
    /// <returns>The schema, stream, and disposable source handle.</returns>
    ValueTask<ReportMaterialization> MaterializeAsync(
        PlanNode            plan,
        QueryInsightRequest request,
        ClaimsPrincipal?    principal,
        CancellationToken   ct);
}

/// <summary>Single-pass report data returned by <see cref="IReportMaterializer" />.</summary>
public sealed class ReportMaterialization : IAsyncDisposable
{
    private readonly IAsyncDisposable? _owner;

    /// <summary>Creates a report materialization from a schema, row stream, and optional source owner.</summary>
    /// <param name="schema">The row schema.</param>
    /// <param name="rows">The unpaged row stream.</param>
    /// <param name="owner">The disposable source resource that owns the stream.</param>
    public ReportMaterialization(
        ImmutableArray<FieldDescriptor>                        schema,
        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> rows,
        IAsyncDisposable?                                      owner = null
    ) {
        Schema  = schema;
        Rows    = rows;
        _owner  = owner;
    }

    /// <summary>Schema for every streamed row.</summary>
    public ImmutableArray<FieldDescriptor> Schema { get; }

    /// <summary>Unpaged row stream.</summary>
    public IAsyncEnumerable<IReadOnlyDictionary<string, object?>> Rows { get; }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _owner?.DisposeAsync() ?? ValueTask.CompletedTask;
}

/// <summary>Production adapter that delegates report materialization to <see cref="PlanExecutor" />.</summary>
public sealed class DefaultReportMaterializer(PlanExecutor executor) : IReportMaterializer
{
    /// <inheritdoc />
    public async ValueTask<ReportMaterialization> MaterializeAsync(
        PlanNode            plan,
        QueryInsightRequest request,
        ClaimsPrincipal?    principal,
        CancellationToken   ct
    ) {
        var materialized = await executor.MaterializeAsync(plan, request, principal, ct: ct);
        return new(materialized.Schema, materialized.Rows, materialized);
    }
}
