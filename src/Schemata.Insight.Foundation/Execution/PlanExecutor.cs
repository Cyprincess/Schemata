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
/// <remarks>
///     A top-level <see cref="LimitNode" /> is applied by residual pagination. Expression-selection
///     values are evaluated by the local pipeline; source materialization supplies their input fields.
/// </remarks>
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
            ? await ExecuteJoinAsync(root, request, principal, true, skip, pageSize, mode, ct)
            : await ExecuteSingleAsync(root, request, principal, true, skip, pageSize, mode, ct);
    }

    /// <summary>
    ///     Opens an unpaged, single-pass row stream for a plan. The top-level pagination limit and all
    ///     request pagination fields are ignored; nested limits remain part of the query plan.
    /// </summary>
    /// <param name="plan">The logical plan to execute.</param>
    /// <param name="request">The source request context.</param>
    /// <param name="principal">The execution principal, when security is enforced.</param>
    /// <param name="enforceSecurity">Whether source drivers enforce their security checks.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The opened schema and unpaged row stream.</returns>
    public ValueTask<MaterializedQuery> MaterializeAsync(
        PlanNode            plan,
        QueryInsightRequest request,
        ClaimsPrincipal?    principal,
        bool                enforceSecurity = true,
        CancellationToken   ct = default
    ) {
        var root = plan is LimitNode limit ? limit.Input : plan;
        if (root.SourceSet.Count > 1) {
            return ValueTask.FromResult(new MaterializedQuery(
                TerminalSchema(root),
                Evaluate(root, request, principal, enforceSecurity, ct)
            ));
        }

        return MaterializeSingleAsync(root, request, principal, enforceSecurity, ct);
    }

    private async ValueTask<QueryInsightResponse> ExecuteSingleAsync(
        PlanNode            root,
        QueryInsightRequest request,
        ClaimsPrincipal?    principal,
        bool                enforceSecurity,
        int                 skip,
        int                 pageSize,
        TotalSizeMode       mode,
        CancellationToken   ct
    ) {
        var source = FindSource(root);
        if (source is null) {
            throw new InsightValidationException(InsightReasons.InvalidArgument, "The plan has no source.");
        }

        var driver = _services.GetKeyedService<ISourceDriver>(source.Config.DriverName);
        if (driver is null) {
            throw new InsightValidationException(
                InsightReasons.Unimplemented,
                $"No driver '{source.Config.DriverName}' is registered."
            );
        }

        var (pushable, localStages) = Split(root, driver.Capabilities);
        var subPlan = new SubPlan(pushable, source.Alias, source.Config) { EnforceSecurity = enforceSecurity };

        await using var result = await driver.ExecuteAsync(subPlan, request, principal, ct);

        // An Estimated total over a local pipeline is the pushed-superset size: an upper bound on the
        // post-local row count, never the exact final count. The driver rows are buffered once so the
        // bound and the page come from a single drive of the source.
        var estimateSuperset = mode is TotalSizeMode.Estimated && localStages.Count > 0;

        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> rows;
        int?                                                   estimate = null;
        if (estimateSuperset) {
            var buffer = new List<IReadOnlyDictionary<string, object?>>();
            await foreach (var row in result.Rows.WithCancellation(ct)) {
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
                                               ct);

        return new() {
            Rows          = page,
            Schema        = ResultSchema(result.Schema, localStages),
            NextPageToken = hasMore ? InsightPageToken.Encode(skip + pageSize) : null,
            TotalSize     = mode is TotalSizeMode.None ? null : estimate ?? total,
        };
    }

    private async ValueTask<MaterializedQuery> MaterializeSingleAsync(
        PlanNode            root,
        QueryInsightRequest request,
        ClaimsPrincipal?    principal,
        bool                enforceSecurity,
        CancellationToken   ct
    ) {
        var source = FindSource(root);
        if (source is null) {
            throw new InsightValidationException(InsightReasons.InvalidArgument, "The plan has no source.");
        }

        var driver = _services.GetKeyedService<ISourceDriver>(source.Config.DriverName);
        if (driver is null) {
            throw new InsightValidationException(
                InsightReasons.Unimplemented,
                $"No driver '{source.Config.DriverName}' is registered."
            );
        }

        var (pushable, localStages) = Split(root, driver.Capabilities);
        var subPlan = new SubPlan(pushable, source.Alias, source.Config) { EnforceSecurity = enforceSecurity };
        var result = await driver.ExecuteAsync(subPlan, request, principal, ct);
        try {
            var rows = localStages.Count == 0
                ? result.Rows
                : _local.RunAsync(result.Rows, source.Alias, localStages, ct);

            return new(ResultSchema(result.Schema, localStages), rows, result);
        } catch {
            await result.DisposeAsync();
            throw;
        }
    }

    private async ValueTask<QueryInsightResponse> ExecuteJoinAsync(
        PlanNode            root,
        QueryInsightRequest request,
        ClaimsPrincipal?    principal,
        bool                enforceSecurity,
        int                 skip,
        int                 pageSize,
        TotalSizeMode       mode,
        CancellationToken   ct
    ) {
        var rows       = Evaluate(root, request, principal, enforceSecurity, ct);
        var countExact = mode is not TotalSizeMode.None;

        var (page, hasMore, total) = await ResidualPage.ScanAsync(
                                               rows,
                                               static _ => true,
                                               skip,
                                               pageSize,
                                               _options.MaxResidualScanRows,
                                               countExact,
                                               ct);

        return new() {
            Rows          = page,
            Schema        = TerminalSchema(root),
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
        bool                                      enforceSecurity,
        [EnumeratorCancellation] CancellationToken ct
    ) {
        if (node.SourceSet.Count <= 1) {
            await foreach (var row in DriveSubtree(node, request, principal, enforceSecurity, ct)) {
                yield return row;
            }

            yield break;
        }

        if (node is JoinNode join) {
            var left  = Evaluate(join.Left, request, principal, enforceSecurity, ct);
            var right = Evaluate(join.Right, request, principal, enforceSecurity, ct);
            await foreach (var row in _local.JoinAsync(left, right, join.On, join.Kind, ct)) {
                yield return row;
            }

            yield break;
        }

        var input = Evaluate(Child(node), request, principal, enforceSecurity, ct);
        await foreach (var row in _local.RunStagesAsync(input, [node], ct)) {
            yield return row;
        }
    }

    private async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> DriveSubtree(
        PlanNode                                  node,
        QueryInsightRequest                       request,
        ClaimsPrincipal?                          principal,
        bool                                      enforceSecurity,
        [EnumeratorCancellation] CancellationToken ct
    ) {
        var source = FindSource(node);
        if (source is null) {
            throw new InsightValidationException(InsightReasons.InvalidArgument, "A join input has no source.");
        }

        var driver = _services.GetKeyedService<ISourceDriver>(source.Config.DriverName);
        if (driver is null) {
            throw new InsightValidationException(
                InsightReasons.Unimplemented,
                $"No driver '{source.Config.DriverName}' is registered."
            );
        }

        var (pushable, localStages) = Split(node, driver.Capabilities);
        var subPlan = new SubPlan(pushable, source.Alias, source.Config) { EnforceSecurity = enforceSecurity };

        await using var result = await driver.ExecuteAsync(subPlan, request, principal, ct);

        await foreach (var row in _local.RunAsync(result.Rows, source.Alias, localStages, ct)) {
            yield return row;
        }
    }

    // Built before the lazy per-source drivers open; field descriptors stay generic and follow
    // the local Select output's names and order.
    private static ImmutableArray<FieldDescriptor> TerminalSchema(PlanNode root) {
        if (FindSelection(root) is not { Items.IsDefaultOrEmpty: false } selection) {
            return [];
        }

        var fields = ImmutableArray.CreateBuilder<FieldDescriptor>(selection.Items.Length);
        AddSelectionFields(selection.Items, false, [], fields);
        AddSelectionFields(selection.Items, true, [], fields);
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
    ///     Splits a single-source plan at the first local stage, using the selected driver's declared
    ///     capabilities to form the pushed prefix.
    /// </summary>
    private static (PlanNode Pushable, IReadOnlyList<PlanNode> Local) Split(
        PlanNode           root,
        DriverCapabilities capabilities
    ) {
        var chain = new List<PlanNode>();
        for (var node = root; node is not SourceNode;) {
            chain.Add(node);
            node = Child(node);
        }

        var source = FindSource(root)!;

        var barrier = -1;
        for (var i = chain.Count - 1; i >= 0; i--) {
            if (!CanPush(chain[i], capabilities)) {
                barrier = i;
                break;
            }
        }

        if (barrier < 0) {
            return (root, []);
        }

        var terminalSelection = FindTerminalSelection(chain, barrier);
        var pushSelection = terminalSelection is not null && HasNested(terminalSelection)
                         && capabilities.HasFlag(DriverCapabilities.Nested)
            ? terminalSelection with { Items = NestedItems(terminalSelection.Items) }
            : null;

        PlanNode pushable = source;
        for (var i = chain.Count - 1; i > barrier; i--) {
            pushable = WithInput(chain[i], pushable);
        }

        if (pushSelection is not null) {
            pushable = pushSelection with { Input = pushable };
        }

        var local = new List<PlanNode>(barrier + 1);
        for (var i = barrier; i >= 0; i--) {
            local.Add(chain[i]);
        }

        return (pushable, local);
    }

    private static bool CanPush(PlanNode node, DriverCapabilities capabilities) {
        if (node is LimitNode) {
            return false;
        }

        if (node is SelectionNode selection) {
            var hasExpression = HasExpression(selection);
            var hasNested     = HasNested(selection);

            // Expression selections are fully local: the driver supplies raw input fields, while the
            // local pipeline evaluates the expression and assigns its output alias.
            if (hasExpression && !hasNested) {
                return false;
            }

            // Nested selections are two-sided: the driver materializes child collections, and the
            // local pipeline applies each child collection's filter, order, limit, and projection.
            // Mixed selections retain this split so local expression evaluation never loses children.
            if (hasNested) {
                return false;
            }

            return capabilities.HasFlag(DriverCapabilities.Project);
        }

        var required = node switch {
            FilterNode  => DriverCapabilities.Filter,
            ComputeNode => DriverCapabilities.Compute,
            GroupNode   => DriverCapabilities.Group,
            OrderNode   => DriverCapabilities.Order,
            var stage   => throw new InsightValidationException(
                               InsightReasons.Unimplemented,
                               $"Plan node '{stage.GetType().Name}' is not a single-source stage."),
        };

        return capabilities.HasFlag(required);
    }

    private static SelectionNode? FindTerminalSelection(IReadOnlyList<PlanNode> chain, int barrier) {
        for (var i = 0; i <= barrier; i++) {
            if (chain[i] is SelectionNode selection) {
                return selection;
            }
        }

        return null;
    }

    private static bool HasExpression(SelectionNode selection) {
        foreach (var item in selection.Items) {
            if (item.Kind is SelectionKind.Expression) {
                return true;
            }
        }

        return false;
    }

    private static bool HasNested(SelectionNode selection) {
        foreach (var item in selection.Items) {
            if (item.Kind is SelectionKind.Nested) {
                return true;
            }
        }

        return false;
    }

    private static ImmutableArray<SelectionItem> NestedItems(ImmutableArray<SelectionItem> items) {
        var nested = ImmutableArray.CreateBuilder<SelectionItem>();
        foreach (var item in items) {
            if (item.Kind is SelectionKind.Nested) {
                nested.Add(item);
            }
        }

        return nested.ToImmutable();
    }

    private static ImmutableArray<FieldDescriptor> ResultSchema(
        IReadOnlyList<FieldDescriptor> driverSchema,
        IReadOnlyList<PlanNode>        localStages
    ) {
        if (localStages.Count == 0 || localStages[^1] is not SelectionNode selection || selection.Items.IsDefaultOrEmpty) {
            return [..driverSchema];
        }

        var fields = ImmutableArray.CreateBuilder<FieldDescriptor>(selection.Items.Length);
        AddSelectionFields(selection.Items, false, driverSchema, fields);
        AddSelectionFields(selection.Items, true, driverSchema, fields);
        return fields.ToImmutable();
    }

    private static void AddSelectionFields(
        ImmutableArray<SelectionItem>                   items,
        bool                                            expressions,
        IReadOnlyList<FieldDescriptor>                  driverSchema,
        ImmutableArray<FieldDescriptor>.Builder         fields
    ) {
        foreach (var item in items) {
            if ((item.Kind is SelectionKind.Expression) != expressions) {
                continue;
            }

            fields.Add(FieldFor(item, driverSchema));
        }
    }

    private static FieldDescriptor FieldFor(SelectionItem item, IReadOnlyList<FieldDescriptor> driverSchema) {
        if (item.Kind is SelectionKind.Expression) {
            return new(item.Alias, FieldType.Object, null, false, []);
        }

        var path = item.FieldPath;
        var name = path is null ? null : LastSegment(path);
        foreach (var field in driverSchema) {
            if (string.Equals(field.Name, item.Alias, StringComparison.Ordinal)
             || (name is not null && string.Equals(field.Name, name, StringComparison.Ordinal))) {
                return field with { Name = item.Alias };
            }
        }

        return item.Kind is SelectionKind.Nested
            ? new(item.Alias, FieldType.Object, null, true, [])
            : new(item.Alias, FieldType.Object, null, false, []);
    }

    private static string LastSegment(string path) {
        var index = path.LastIndexOf('.');
        return index < 0 ? path : path[(index + 1)..];
    }

    private static PlanNode WithInput(PlanNode node, PlanNode input) {
        return node switch {
            FilterNode filter       => filter with { Input = input },
            ComputeNode compute     => compute with { Input = input },
            GroupNode group         => group with { Input = input },
            OrderNode order         => order with { Input = input },
            LimitNode limit         => limit with { Input = input },
            SelectionNode selection => selection with { Input = input },
            var stage               => throw new InsightValidationException(
                                           InsightReasons.Unimplemented,
                                           $"Plan node '{stage.GetType().Name}' is not a single-source stage."),
        };
    }

    private static async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> ToAsync(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        [EnumeratorCancellation] CancellationToken          ct
    ) {
        foreach (var row in rows) {
            ct.ThrowIfCancellationRequested();
            yield return row;
        }

        await Task.CompletedTask;
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
