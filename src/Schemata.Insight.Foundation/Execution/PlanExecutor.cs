using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Resource;
using Schemata.Expressions.Skeleton;
using Schemata.Insight.Skeleton;

namespace Schemata.Insight.Foundation;

/// <summary>
///     Executes a plan: it strips the top-level pagination, then for a single source splits the plan
///     into a driver-pushable prefix and the local stages the driver cannot push; for multiple sources
///     it drives each source independently and joins / aggregates locally. Pagination and the
///     total-size mode are applied through <see cref="ResidualPage" />.
/// </summary>
public sealed class PlanExecutor
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize     = 100;

    private readonly LocalPipelineExecutor  _local;
    private readonly SchemataInsightOptions _options;
    private readonly IServiceProvider       _services;

    /// <summary>Wires the plan executor with keyed-driver resolution, the local pipeline fallback, and the configured Insight options.</summary>
    /// <param name="services">The provider resolving keyed source drivers.</param>
    /// <param name="local">The local pipeline executor for stages the driver cannot push.</param>
    /// <param name="options">The Insight options (total-size mode, scan cap).</param>
    public PlanExecutor(
        IServiceProvider                 services,
        LocalPipelineExecutor            local,
        IOptions<SchemataInsightOptions> options
    ) {
        _services = services;
        _local    = local;
        _options  = options.Value;
    }

    /// <summary>Executes the plan and builds the response.</summary>
    public async ValueTask<QueryInsightResponse> ExecuteAsync(
        PlanNode            plan,
        QueryInsightRequest request,
        ClaimsPrincipal?    principal,
        CancellationToken   ct
    ) {
        int? requestedSkip = null;
        int? requestedTake = null;
        var  root          = plan;
        if (root is LimitNode limit) {
            requestedSkip = limit.Skip;
            requestedTake = limit.Take;
            root          = limit.Input;
        }

        var pageSize = ClampPageSize(requestedTake);
        var skip = request.PageToken is { Length: > 0 } token
            ? InsightPageToken.Decode(token)
            : Math.Max(0, requestedSkip ?? 0);
        var mode = _options.TotalSize is TotalSizeMode.Default ? TotalSizeMode.Exact : _options.TotalSize;

        return root.SourceSet.Count > 1
            ? await ExecuteJoinAsync(root, request, principal, skip, pageSize, mode, ct).ConfigureAwait(false)
            : await ExecuteSingleAsync(root, request, principal, skip, pageSize, mode, ct).ConfigureAwait(false);
    }

    private async ValueTask<QueryInsightResponse> ExecuteSingleAsync(
        PlanNode            root,
        QueryInsightRequest request,
        ClaimsPrincipal?    principal,
        int                 skip,
        int                 pageSize,
        TotalSizeMode       mode,
        CancellationToken   ct
    ) {
        var source = FindSource(root)
                  ?? throw new InsightValidationException(InsightReasons.InvalidArgument, "The plan has no source.");

        var driver = _services.GetKeyedService<ISourceDriver>(source.Config.DriverName)
                  ?? throw new InsightValidationException(InsightReasons.Unimplemented,
                                                         $"No driver '{source.Config.DriverName}' is registered.");

        var (pushable, localStages) = Split(root);
        var subPlan = new SubPlan(pushable, source.Alias, source.Config);

        await using var result = await driver.ExecuteAsync(subPlan, request, principal, ct).ConfigureAwait(false);

        // An Estimated total over a local pipeline is the pushed-superset size: an upper bound on the
        // post-local row count, never the exact final count. The driver rows are buffered once so the
        // bound and the page come from a single drive of the source.
        var estimateSuperset = mode is TotalSizeMode.Estimated && localStages.Count > 0;

        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> rows;
        int?                                                   estimate = null;
        if (estimateSuperset) {
            var buffer = new List<IReadOnlyDictionary<string, object?>>();
            await foreach (var row in result.Rows.WithCancellation(ct).ConfigureAwait(false)) {
                buffer.Add(row);
            }

            estimate = buffer.Count;
            rows     = _local.RunAsync(ToAsync(buffer, ct), source.Alias, localStages, ct);
        } else {
            rows = localStages.Count == 0
                ? result.Rows
                : _local.RunAsync(result.Rows, source.Alias, localStages, ct);
        }

        var countExact = mode is TotalSizeMode.Exact || (mode is TotalSizeMode.Estimated && !estimateSuperset);

        var (page, hasMore, total) = await ResidualPage.ScanAsync(
                                               rows,
                                               static _ => true,
                                               skip,
                                               pageSize,
                                               _options.MaxResidualScanRows,
                                               countExact,
                                               ct)
                                          .ConfigureAwait(false);

        return new() {
            Rows          = page,
            Schema        = [..result.Schema],
            NextPageToken = hasMore ? InsightPageToken.Encode(skip + pageSize) : null,
            TotalSize     = mode is TotalSizeMode.None ? null : estimate ?? total,
        };
    }

    private async ValueTask<QueryInsightResponse> ExecuteJoinAsync(
        PlanNode            root,
        QueryInsightRequest request,
        ClaimsPrincipal?    principal,
        int                 skip,
        int                 pageSize,
        TotalSizeMode       mode,
        CancellationToken   ct
    ) {
        var rows       = Evaluate(root, request, principal, ct);
        var countExact = mode is not TotalSizeMode.None;

        var (page, hasMore, total) = await ResidualPage.ScanAsync(
                                               rows,
                                               static _ => true,
                                               skip,
                                               pageSize,
                                               _options.MaxResidualScanRows,
                                               countExact,
                                               ct)
                                          .ConfigureAwait(false);

        return new() {
            Rows          = page,
            Schema        = JoinSchema(root),
            NextPageToken = hasMore ? InsightPageToken.Encode(skip + pageSize) : null,
            TotalSize     = mode is TotalSizeMode.None ? null : total,
        };
    }

    /// <summary>
    ///     Produces alias-nested rows for a plan subtree. A single-source subtree is driven through its
    ///     driver; a <see cref="JoinNode" /> joins its evaluated inputs; a multi-source unary stage runs
    ///     over its evaluated input. The terminal selection flattens to the response shape.
    /// </summary>
    private async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> Evaluate(
        PlanNode                                  node,
        QueryInsightRequest                       request,
        ClaimsPrincipal?                          principal,
        [EnumeratorCancellation] CancellationToken ct
    ) {
        if (node.SourceSet.Count <= 1) {
            await foreach (var row in DriveSubtree(node, request, principal, ct).ConfigureAwait(false)) {
                yield return row;
            }

            yield break;
        }

        if (node is JoinNode join) {
            var left  = Evaluate(join.Left, request, principal, ct);
            var right = Evaluate(join.Right, request, principal, ct);
            await foreach (var row in _local.JoinAsync(left, right, join.On, join.Kind, ct).ConfigureAwait(false)) {
                yield return row;
            }

            yield break;
        }

        var input = Evaluate(Child(node), request, principal, ct);
        await foreach (var row in _local.RunStagesAsync(input, [node], ct).ConfigureAwait(false)) {
            yield return row;
        }
    }

    private async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> DriveSubtree(
        PlanNode                                  node,
        QueryInsightRequest                       request,
        ClaimsPrincipal?                          principal,
        [EnumeratorCancellation] CancellationToken ct
    ) {
        var source = FindSource(node)
                  ?? throw new InsightValidationException(InsightReasons.InvalidArgument, "A join input has no source.");

        var driver = _services.GetKeyedService<ISourceDriver>(source.Config.DriverName)
                  ?? throw new InsightValidationException(InsightReasons.Unimplemented,
                                                         $"No driver '{source.Config.DriverName}' is registered.");

        var (pushable, localStages) = Split(node);
        var subPlan = new SubPlan(pushable, source.Alias, source.Config);

        await using var result = await driver.ExecuteAsync(subPlan, request, principal, ct).ConfigureAwait(false);

        await foreach (var row in _local.RunAsync(result.Rows, source.Alias, localStages, ct).ConfigureAwait(false)) {
            yield return row;
        }
    }

    private static ImmutableArray<FieldDescriptor> JoinSchema(PlanNode root) {
        var selection = FindSelection(root);
        if (selection is null || selection.Items.IsDefaultOrEmpty) {
            return [];
        }

        var fields = ImmutableArray.CreateBuilder<FieldDescriptor>(selection.Items.Length);
        foreach (var item in selection.Items) {
            fields.Add(new(item.Alias, FieldType.Object, null, item.Kind is SelectionKind.Nested, []));
        }

        return fields.ToImmutable();
    }

    private static SelectionNode? FindSelection(PlanNode node) {
        return node switch {
            SelectionNode selection => selection,
            FilterNode filter       => FindSelection(filter.Input),
            OrderNode order         => FindSelection(order.Input),
            LimitNode limit         => FindSelection(limit.Input),
            ComputeNode compute     => FindSelection(compute.Input),
            GroupNode group         => FindSelection(group.Input),
            var _                   => null,
        };
    }

    /// <summary>
    ///     Splits the plan at the lowest stage the driver cannot push. Everything from the first
    ///     <see cref="ComputeNode" /> / <see cref="GroupNode" /> upward runs locally; the contiguous
    ///     filter / order chain above the source is handed to the driver. When local stages follow, the
    ///     driver streams the full source row (its selection moves to the local terminal stage) so the
    ///     local stages can reference every source field.
    /// </summary>
    private static (PlanNode Pushable, IReadOnlyList<PlanNode> Local) Split(PlanNode root) {
        var chain = new List<PlanNode>();
        for (var node = root; node is not SourceNode;) {
            chain.Add(node);
            node = Child(node);
        }

        var source = FindSource(root)!;

        var barrier = -1;
        for (var i = chain.Count - 1; i >= 0; i--) {
            if (chain[i] is ComputeNode or GroupNode || IsNestedSelection(chain[i])) {
                barrier = i;
                break;
            }
        }

        if (barrier < 0) {
            return (root, []);
        }

        // A nested selection materializes its child lists in the driver (entity access), then re-runs
        // locally for the child sub-pipelines and the terminal flatten; so it lives in both halves.
        var pushSelection = IsNestedSelection(chain[barrier]) ? chain[barrier] : null;

        PlanNode pushable = source;
        for (var i = chain.Count - 1; i > barrier; i--) {
            pushable = chain[i] switch {
                FilterNode filter => filter with { Input = pushable },
                OrderNode order   => order with { Input = pushable },
                var node          => throw new InsightValidationException(InsightReasons.Unimplemented,
                                                                         $"Stage '{node.GetType().Name}' cannot precede a local barrier on the driver."),
            };
        }

        if (pushSelection is SelectionNode selection) {
            pushable = selection with { Input = pushable };
        }

        var local = new List<PlanNode>(barrier + 1);
        for (var i = barrier; i >= 0; i--) {
            local.Add(chain[i]);
        }

        return (pushable, local);
    }

    private static bool IsNestedSelection(PlanNode node) {
        if (node is not SelectionNode selection) {
            return false;
        }

        foreach (var item in selection.Items) {
            if (item.Kind is SelectionKind.Nested or SelectionKind.Expression) {
                return true;
            }
        }

        return false;
    }

    private static async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> ToAsync(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        [EnumeratorCancellation] CancellationToken          ct
    ) {
        foreach (var row in rows) {
            ct.ThrowIfCancellationRequested();
            yield return row;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private static PlanNode Child(PlanNode node) {
        return node switch {
            FilterNode filter       => filter.Input,
            OrderNode order         => order.Input,
            LimitNode limit         => limit.Input,
            SelectionNode selection => selection.Input,
            ComputeNode compute     => compute.Input,
            GroupNode group         => group.Input,
            var _ => throw new InsightValidationException(InsightReasons.Unimplemented,
                                                         $"Plan node '{node.GetType().Name}' is not a single-source stage."),
        };
    }

    private static SourceNode? FindSource(PlanNode node) {
        return node switch {
            SourceNode source     => source,
            FilterNode filter     => FindSource(filter.Input),
            OrderNode order       => FindSource(order.Input),
            LimitNode limit       => FindSource(limit.Input),
            SelectionNode select  => FindSource(select.Input),
            ComputeNode compute   => FindSource(compute.Input),
            GroupNode group       => FindSource(group.Input),
            var _                 => null,
        };
    }

    private static int ClampPageSize(int? requested) {
        return requested switch {
            null or <= 0 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            var size      => size.Value,
        };
    }
}
