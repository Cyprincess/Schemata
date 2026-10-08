using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Errors;
using Schemata.Entity.Repository;
using Schemata.Expressions.Skeleton;
using Schemata.Insight.Foundation.Execution;
using Schemata.Insight.Foundation.Materialization;
using Schemata.Insight.Foundation.Planning;
using Schemata.Insight.Foundation.Security;
using Schemata.Insight.Skeleton.Drivers;
using Schemata.Insight.Skeleton.Plan;
using Schemata.Insight.Skeleton.Queries;

namespace Schemata.Insight.Foundation.Drivers;

/// <summary>
///     A repository-backed source registered under a source name. Each binding owns its concrete
///     entity and projection so the driver can lower and execute against a closed generic surface.
/// </summary>
internal abstract class RepositorySource
{
    /// <summary>The public row type materialized by <see cref="ExecuteAsync" />.</summary>
    public abstract Type PublicType { get; }

    /// <summary>
    ///     Lowers the subtree, resolves an execution scope, and streams the projected rows. The caller
    ///     owns the returned result's lifetime.
    /// </summary>
    public abstract ValueTask<ISourceResult> ExecuteAsync(
        IServiceProvider    services,
        SubPlan             plan,
        QueryInsightRequest request,
        ClaimsPrincipal?    principal,
        CancellationToken   ct = default);
}

/// <summary>
///     A repository source whose projection is fixed at registration time. The row-level entitlement
///     applies against the entity query before projection; pushed filters and ordering compose into
///     the backend query, and the residual half of each filter evaluates locally against the
///     projected rows.
/// </summary>
/// <typeparam name="TEntity">The repository's row type.</typeparam>
/// <typeparam name="TPublic">The shape materialized into rows.</typeparam>
internal sealed class RepositorySource<TEntity, TPublic>(Expression<Func<TEntity, TPublic>> projection) : RepositorySource
    where TEntity : class
    where TPublic : class {
    private readonly Expression<Func<TEntity, TPublic>> _projection = projection;

    public override Type PublicType => typeof(TPublic);

    public override async ValueTask<ISourceResult> ExecuteAsync(
        IServiceProvider    services,
        SubPlan             plan,
        QueryInsightRequest request,
        ClaimsPrincipal?    principal,
        CancellationToken   ct = default
    ) {
        var scope = services.CreateAsyncScope();
        try {
            var provider = scope.ServiceProvider;

            Expression<Func<TEntity, bool>>? entitlement = null;
            if (plan.EnforceSecurity) {
                entitlement = await InsightSecurityGate.AuthorizeAsync<TEntity>(request, principal, provider, ct);
            }

            var shape     = Lower(plan.Root);
            var compiled  = CompileFilters(shape, provider);

            var repo    = provider.GetRequiredService<IRepository<TEntity>>();
            var rows    = Rows(repo, entitlement, shape, compiled, provider, plan.SourceAlias, ct);
            var schema = SchemaBuilder.For(typeof(TPublic),
                shape.Items.Any(item => item.Kind == SelectionKind.Nested) ? [] : shape.Items, plan.SourceAlias);

            return new RepositorySourceResult(rows, schema, scope);
        } catch {
            await scope.DisposeAsync();
            throw;
        }
    }

    private static CompiledFilters CompileFilters(PlanShape shape, IServiceProvider provider) {
        var pushedExpressions = new List<Expression<Func<TPublic, bool>>>(shape.Filters.Length);
        var residuals         = new List<Func<TPublic, bool>>(shape.Filters.Length);

        foreach (var filter in shape.Filters) {
            var compiler = provider.GetRequiredKeyedService<IExpressionCompiler>(filter.Predicate.Language);
            var planner  = provider.GetRequiredKeyedService<IExpressionPushdownPlanner>(filter.Predicate.Language);
            var plan     = planner.Plan(filter.Predicate.Tree, ExpressionCapabilities.Relational);

            if (plan.Pushed is not null) {
                pushedExpressions.Add(compiler.Compile<TPublic, bool>(plan.Pushed));
            }

            if (plan.Residual is not null) {
                residuals.Add(ExpressionCache.GetOrAddDelegate(compiler.Compile<TPublic, bool>(plan.Residual)));
            }
        }

        return new(pushedExpressions, residuals);
    }

    private IAsyncEnumerable<IReadOnlyDictionary<string, object?>> Rows(
        IRepository<TEntity>                              repo,
        Expression<Func<TEntity, bool>>?                 entitlement,
        PlanShape                                         shape,
        CompiledFilters                                   compiled,
        IServiceProvider                                  provider,
        string                                            alias,
        CancellationToken                                 ct
    ) {
        var rows = repo.ListAsync<TPublic>(q => BuildQuery(q, entitlement, shape, compiled, provider), ct);
        var cap = provider.GetService<IOptions<SchemataInsightOptions>>()?.Value.MaxResidualScanRows ?? 10_000;
        return Materialize(LocalPipelineExecutor.Scan(rows, cap, ct), compiled.Residuals, shape.Items, alias, ct);
    }

    private IQueryable<TPublic> BuildQuery(
        IQueryable<TEntity>              source,
        Expression<Func<TEntity, bool>>? entitlement,
        PlanShape                        shape,
        CompiledFilters                  compiled,
        IServiceProvider                 provider
    ) {
        var query = source;
        if (entitlement is not null) {
            query = query.Where(entitlement);
        }

        var publicQuery = query.Select(_projection);

        foreach (var pushed in compiled.PushedExpressions) {
            publicQuery = publicQuery.Where(pushed);
        }

        if (shape.Order is not null) {
            publicQuery = provider.GetRequiredService<IOrderCompiler>().CompileOrder<TPublic>(shape.Order.OrderBy)(publicQuery);
        }

        return publicQuery;
    }

    private static async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> Materialize(
        IAsyncEnumerable<TPublic>                         rows,
        IReadOnlyList<Func<TPublic, bool>>                residuals,
        ImmutableArray<SelectionItem>                     items,
        string                                            alias,
        [EnumeratorCancellation] CancellationToken       ct
    ) {
        await foreach (var row in rows.WithCancellation(ct)) {
            if (residuals.All(residual => residual(row))) {
                yield return RowMaterializer.ToRow(row, items, alias);
            }
        }
    }

    private sealed record CompiledFilters(
        IReadOnlyList<Expression<Func<TPublic, bool>>> PushedExpressions,
        IReadOnlyList<Func<TPublic, bool>>             Residuals);

    private static PlanShape Lower(PlanNode root) {
        var filters = ImmutableArray.CreateBuilder<FilterNode>();
        OrderNode? order = null;
        var items = ImmutableArray<SelectionItem>.Empty;

        Visit(root);
        return new(filters.ToImmutable(), order, items);

        void Visit(PlanNode node) {
            switch (node) {
                case SelectionNode selection:
                    Visit(selection.Input);
                    items = selection.Items;
                    break;
                case LimitNode limit:
                    Visit(limit.Input);
                    break;
                case OrderNode orderNode:
                    Visit(orderNode.Input);
                    order = orderNode;
                    break;
                case FilterNode filter:
                    Visit(filter.Input);
                    filters.Add(filter);
                    break;
                case SourceNode:
                    break;
                default:
                    throw new InsightValidationException(InsightReasons.Unimplemented,
                        SchemataResources.INSIGHT_NODE_UNSUPPORTED,
                        new Dictionary<string, string?> { ["node"] = node.GetType().Name });
            }
        }
    }

    private sealed record PlanShape(
        ImmutableArray<FilterNode>    Filters,
        OrderNode?                    Order,
        ImmutableArray<SelectionItem> Items);
}
