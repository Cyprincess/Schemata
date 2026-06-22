using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Expressions.Skeleton;
using Schemata.Insight.Skeleton;

namespace Schemata.Insight.Foundation;

/// <summary>
///     Builds the single-source logical plan for a request: source resolution, a filter / order /
///     compute / group-by pipeline, flat projection, and top-level pagination. Value-kind slots
///     (compute, aggregation arguments) require a value-capable language. Joins, mid-pipeline limits,
///     and nested selections are rejected in this phase.
/// </summary>
public sealed class InsightPlanBuilder
{
    private readonly IReadOnlyList<IInsightSourceCatalog> _catalogs;
    private readonly SchemataInsightOptions               _options;
    private readonly IServiceProvider                     _services;

    /// <summary>Creates the plan builder.</summary>
    /// <param name="catalogs">The source catalogs, highest priority first; a name resolves against the first that knows it.</param>
    /// <param name="services">The provider resolving keyed compilers and the order compiler.</param>
    /// <param name="options">The Insight options (default language).</param>
    public InsightPlanBuilder(
        IEnumerable<IInsightSourceCatalog> catalogs,
        IServiceProvider                   services,
        IOptions<SchemataInsightOptions>   options
    ) {
        _catalogs = catalogs.ToList();
        _services = services;
        _options  = options.Value;
    }

    /// <summary>Builds the logical plan for the request.</summary>
    /// <param name="request">The query request.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The root plan node.</returns>
    public async ValueTask<PlanNode> BuildAsync(QueryInsightRequest request, CancellationToken ct) {
        if (request.Sources.Count == 0) {
            throw new InsightValidationException(InsightReasons.InvalidArgument, "At least one source is required.");
        }

        var configs  = new Dictionary<string, SourceConfig>(StringComparer.Ordinal);
        var subtrees = new Dictionary<string, PlanNode>(StringComparer.Ordinal);
        foreach (var binding in request.Sources) {
            if (configs.ContainsKey(binding.Alias)) {
                throw new InsightValidationException(InsightReasons.InvalidArgument,
                                                    $"Duplicate source alias '{binding.Alias}'.");
            }

            var config = await ResolveAsync(binding.Name, ct)
                      ?? throw new InsightValidationException(InsightReasons.UnknownSourceName,
                                                             $"Unknown source '{binding.Name}'.",
                                                             new Dictionary<string, string> { ["name"] = binding.Name });

            configs[binding.Alias]  = config;
            subtrees[binding.Alias] = new SourceNode(binding.Alias, config) {
                SourceSet = ImmutableHashSet.Create(binding.Alias),
            };
        }

        var node = FoldJoins(request, subtrees);

        foreach (var transform in request.Transformations) {
            node = ApplyTransform(node, transform, request, node.SourceSet, allowLimit: false);
        }

        node = new SelectionNode(node, BuildSelections(request.Selections, request, configs)) { SourceSet = node.SourceSet };

        return new LimitNode(node, request.Skip, request.PageSize) { SourceSet = node.SourceSet };
    }

    // Resolves a name against the catalogs in priority order, returning the first match.
    private async ValueTask<SourceConfig?> ResolveAsync(string name, CancellationToken ct) {
        foreach (var catalog in _catalogs) {
            var config = await catalog.ResolveAsync(name, ct).ConfigureAwait(false);
            if (config is not null) {
                return config;
            }
        }

        return null;
    }

    private PlanNode FoldJoins(QueryInsightRequest request, Dictionary<string, PlanNode> subtrees) {
        foreach (var join in request.Joins) {
            if (join.Kind is JoinKind.Unspecified) {
                throw new InsightValidationException(InsightReasons.InvalidArgument, "A join must specify a kind.");
            }

            var left  = Subtree(subtrees, join.Left);
            var right = Subtree(subtrees, join.Right);
            if (ReferenceEquals(left, right)) {
                throw new InsightValidationException(InsightReasons.InvalidArgument,
                                                    $"Join aliases '{join.Left}' and '{join.Right}' are already joined.");
            }

            var merged = new JoinNode(left, right, join.Kind, ParsePredicate(join.On, request)) {
                SourceSet = left.SourceSet.Union(right.SourceSet),
            };
            foreach (var alias in merged.SourceSet) {
                subtrees[alias] = merged;
            }
        }

        var root = subtrees[request.Sources[0].Alias];
        if (root.SourceSet.Count != request.Sources.Count) {
            throw new InsightValidationException(InsightReasons.InvalidArgument,
                                                "All sources must be connected by joins.");
        }

        return root;
    }

    private static PlanNode Subtree(Dictionary<string, PlanNode> subtrees, string alias) {
        return subtrees.TryGetValue(alias, out var node)
            ? node
            : throw new InsightValidationException(InsightReasons.InvalidArgument, $"Join references unknown alias '{alias}'.");
    }

    private PlanNode ApplyTransform(
        PlanNode                 input,
        TransformationSpec       transform,
        QueryInsightRequest      request,
        ImmutableHashSet<string> sourceSet,
        bool                     allowLimit
    ) {
        if (transform.Filter is { } filter) {
            return new FilterNode(input, ParsePredicate(filter.Predicate, request)) { SourceSet = sourceSet };
        }

        if (transform.OrderBy is { } order) {
            try {
                _services.GetRequiredService<IOrderCompiler>().Parse(order.OrderBy);
            } catch (ArgumentException ex) {
                throw new InsightValidationException(InsightReasons.InvalidArgument, ex.Message);
            }

            return new OrderNode(input, order.OrderBy) { SourceSet = sourceSet };
        }

        if (transform.Compute is { } compute) {
            var fields = ImmutableArray.CreateBuilder<ComputedField>(compute.Fields.Length);
            foreach (var field in compute.Fields) {
                fields.Add(new(field.Alias, ParseValue(field.Expression, request)));
            }

            return new ComputeNode(input, fields.ToImmutable()) { SourceSet = sourceSet };
        }

        if (transform.GroupBy is { } group) {
            var aggregations = ImmutableArray.CreateBuilder<Aggregation>(group.Aggregations.Length);
            foreach (var aggregation in group.Aggregations) {
                aggregations.Add(new(aggregation.Alias, aggregation.Function, aggregation.Field));
            }

            return new GroupNode(input, group.Keys, aggregations.ToImmutable()) { SourceSet = sourceSet };
        }

        if (transform.Top is not null || transform.Skip is not null) {
            if (!allowLimit) {
                throw new InsightValidationException(InsightReasons.Unimplemented,
                                                    "Mid-pipeline top/skip is not supported at the top level; use top-level page_size/skip.");
            }

            return new LimitNode(input, transform.Skip?.Count, transform.Top?.Count) { SourceSet = sourceSet };
        }

        throw new InsightValidationException(InsightReasons.InvalidArgument,
                                            "A transformation must set exactly one operation.");
    }

    private ParsedExpression ParsePredicate(InsightExpression expression, QueryInsightRequest request) {
        return Parse(expression, request, ExpressionKind.Predicate);
    }

    private ParsedExpression ParseValue(InsightExpression expression, QueryInsightRequest request) {
        return Parse(expression, request, ExpressionKind.Value);
    }

    private ParsedExpression Parse(InsightExpression expression, QueryInsightRequest request, ExpressionKind kind) {
        var language = expression.Language ?? request.Language ?? _options.DefaultLanguage;
        var compiler = _services.GetKeyedService<IExpressionCompiler>(language)
                    ?? throw new InsightValidationException(InsightReasons.UnknownExpressionLanguage,
                                                           $"Unknown expression language '{language}'.",
                                                           new Dictionary<string, string> { ["language"] = language });

        if (kind is ExpressionKind.Value) {
            var descriptor = _services.GetKeyedService<ExpressionLanguageDescriptor>(language);
            if (descriptor is null || !descriptor.SupportsValues) {
                throw new InsightValidationException(InsightReasons.ExpressionLanguageNotValueCapable,
                                                    $"Language '{language}' cannot compile value expressions.",
                                                    new Dictionary<string, string> { ["language"] = language });
            }
        }

        try {
            return new(compiler.Parse(expression.Source), language, kind);
        } catch (Exception ex) when (ex is ExpressionException or ArgumentException) {
            throw new InsightValidationException(InsightReasons.InvalidExpression, $"Invalid expression '{expression.Source}'.");
        }
    }

    private ImmutableArray<SelectionItem> BuildSelections(
        IList<SelectionSpec>                      selections,
        QueryInsightRequest                       request,
        IReadOnlyDictionary<string, SourceConfig> configs
    ) {
        var items = ImmutableArray.CreateBuilder<SelectionItem>(selections.Count);

        foreach (var selection in selections) {
            if (selection.Selections.Count > 0) {
                items.Add(BuildNested(selection, request, configs));
                continue;
            }

            if (selection.Transformations.Count > 0) {
                throw new InsightValidationException(InsightReasons.InvalidArgument,
                                                    "A flat selection cannot carry transformations.");
            }

            if (selection.Expression is { } expression) {
                var alias = selection.Alias
                         ?? throw new InsightValidationException(InsightReasons.InvalidArgument,
                                                                "A computed selection requires an alias.");
                items.Add(new(alias, SelectionKind.Expression, null, ParseValue(expression, request), [], null));
                continue;
            }

            if (string.IsNullOrWhiteSpace(selection.Field)) {
                throw new InsightValidationException(InsightReasons.InvalidArgument, "A selection must specify a field.");
            }

            var fieldAlias = selection.Alias ?? LastSegment(selection.Field);
            items.Add(new(fieldAlias, SelectionKind.Field, selection.Field, null, [], null));
        }

        return items.ToImmutable();
    }

    private SelectionItem BuildNested(
        SelectionSpec                             selection,
        QueryInsightRequest                       request,
        IReadOnlyDictionary<string, SourceConfig> configs
    ) {
        if (string.IsNullOrWhiteSpace(selection.Field)) {
            throw new InsightValidationException(InsightReasons.InvalidArgument,
                                                "A nested selection must name its navigation field.");
        }

        var config      = ParentConfig(selection.Field, configs);
        var childAlias  = ChildAlias(selection);
        var childConfig = config with {
            Params = new Dictionary<string, object?>(config.Params) { ["navigation"] = LastSegment(selection.Field) },
        };
        var childSet  = ImmutableHashSet.Create(childAlias);
        var childMap  = new Dictionary<string, SourceConfig>(StringComparer.Ordinal) { [childAlias] = childConfig };

        PlanNode node = new SourceNode(childAlias, childConfig) { SourceSet = childSet };
        foreach (var transform in selection.Transformations) {
            node = ApplyTransform(node, transform, request, childSet, allowLimit: true);
        }

        var childItems = BuildSelections(selection.Selections, request, childMap);
        node = new SelectionNode(node, childItems) { SourceSet = childSet };

        var alias = selection.Alias ?? LastSegment(selection.Field);
        return new(alias, SelectionKind.Nested, selection.Field, null, childItems, node);
    }

    private static SourceConfig ParentConfig(string field, IReadOnlyDictionary<string, SourceConfig> configs) {
        var dot = field.IndexOf('.');
        if (dot >= 0 && configs.TryGetValue(field[..dot], out var byAlias)) {
            return byAlias;
        }

        if (configs.Count == 1) {
            foreach (var config in configs.Values) {
                return config;
            }
        }

        throw new InsightValidationException(InsightReasons.InvalidArgument,
                                            $"A nested selection '{field}' must name an existing source alias.");
    }

    private static string ChildAlias(SelectionSpec selection) {
        foreach (var child in selection.Selections) {
            if (child.Field is { } path && path.IndexOf('.') is var dot and >= 0) {
                return path[..dot];
            }
        }

        return LastSegment(selection.Field!);
    }

    private static string LastSegment(string path) {
        var index = path.LastIndexOf('.');
        return index < 0 ? path : path[(index + 1)..];
    }
}
