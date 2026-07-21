using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Humanizer;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Expressions.Skeleton;
using Schemata.Insight.Skeleton;

namespace Schemata.Insight.Foundation;

public sealed class RepositoryDriver(IServiceProvider services) : ISourceDriver
{
    private static readonly ConcurrentDictionary<string, Type> EntityTypes = new(StringComparer.Ordinal);

    // Nested selections need the parent's navigation collections eager-loaded. The EF Core string
    // Include lives outside this module's compile graph, so it is bound reflectively: EF queryables
    // receive the include; providers without this extension skip it.
    private static readonly Lazy<MethodInfo?> EfInclude = new(ResolveEfInclude);

    /// <summary>The keyed name under which this driver registers and sources reference it.</summary>
    public const string DriverName = "repository";

    private readonly IServiceProvider _services = services;

    public string Name => DriverName;

    public DriverCapabilities Capabilities
        => DriverCapabilities.Filter
         | DriverCapabilities.Project
         | DriverCapabilities.Order
         | DriverCapabilities.Nested;

    public async ValueTask<ISourceResult> ExecuteAsync(
        SubPlan             subPlan,
        QueryInsightRequest request,
        ClaimsPrincipal?    principal,
        CancellationToken   ct = default
    ) {
        if (!subPlan.Config.Params.TryGetValue("resource", out var value) || value is not string resource) {
            throw new InsightValidationException(InsightReasons.InvalidArgument, "A repository source requires a resource parameter.");
        }

        var entityType = ResolveEntityType(resource);
        var method = typeof(RepositoryDriver).GetMethod(nameof(ExecuteCoreAsync), BindingFlags.Instance | BindingFlags.NonPublic)!
                                             .MakeGenericMethod(entityType);
        var task = (Task<ISourceResult>)method.Invoke(this, [subPlan, request, principal, ct])!;

        return await task;
    }

    private static Type ResolveEntityType(string resource) {
        return EntityTypes.GetOrAdd(resource, static key => {
            foreach (var type in AppDomainTypeCache.Types.Values) {
                if (!typeof(ICanonicalName).IsAssignableFrom(type) || type is not { IsClass: true, IsAbstract: false }) {
                    continue;
                }

                if (ResourceNameDescriptor.ForType(type).Collection == key) {
                    return type;
                }
            }

            throw new InsightValidationException(InsightReasons.UnknownSourceName, $"Unknown resource '{key}'.");
        });
    }

    private async Task<ISourceResult> ExecuteCoreAsync<TEntity>(
        SubPlan             subPlan,
        QueryInsightRequest request,
        ClaimsPrincipal?    principal,
        CancellationToken   ct
    ) where TEntity : class {
        var scope = _services.CreateAsyncScope();
        try {
            var services = scope.ServiceProvider;
            Expression? entitlement = null;
            if (subPlan.EnforceSecurity) {
                entitlement = await InsightSecurityGate.AuthorizeAsync(typeof(TEntity), request, principal, services, ct);
            }
            var shape = Lower(subPlan.Root);
            var residuals = new List<Func<TEntity, bool>>();

            IQueryable<TEntity> Query(IQueryable<TEntity> source) {
                var query = source;
                if (entitlement is Expression<Func<TEntity, bool>> e) {
                    query = query.Where(e);
                }

                foreach (var filter in shape.Filters) {
                    var compiler = services.GetRequiredKeyedService<IExpressionCompiler>(filter.Predicate.Language);
                    var planner = services.GetRequiredKeyedService<IExpressionPushdownPlanner>(filter.Predicate.Language);
                    var plan = planner.Plan(filter.Predicate.Tree, ExpressionCapabilities.Relational);
                    if (plan.Pushed is not null) {
                        query = query.Where(compiler.Compile<TEntity, bool>(plan.Pushed));
                    }

                    if (plan.Residual is not null) {
                        residuals.Add(ExpressionCache.GetOrAddDelegate(compiler.Compile<TEntity, bool>(plan.Residual)));
                    }
                }

                if (shape.Order is not null) {
                    query = services.GetRequiredService<IOrderCompiler>().CompileOrder<TEntity>(shape.Order.OrderBy)(query);
                }

                foreach (var navigation in NavigationNames(shape.Items, subPlan.SourceAlias)) {
                    query = Include(query, navigation);
                }

                return query;
            }

            var repo = services.GetRequiredService<IRepository<TEntity>>();
            var entities = repo.ListAsync<TEntity>(q => Query(q), ct);
            var rows = Rows(entities, residuals, shape.Items, subPlan.SourceAlias, ct);
            var schema = SchemaBuilder.For(typeof(TEntity), shape.Items, subPlan.SourceAlias);

            return new RepositorySourceResult(rows, schema, scope);
        } catch {
            await scope.DisposeAsync();
            throw;
        }
    }

    private static IEnumerable<string> NavigationNames(ImmutableArray<SelectionItem> items, string alias) {
        if (items.IsDefaultOrEmpty) {
            yield break;
        }

        var prefix = alias + ".";
        foreach (var item in items) {
            if (item.Kind != SelectionKind.Nested || string.IsNullOrWhiteSpace(item.FieldPath)) {
                continue;
            }

            var path = item.FieldPath.StartsWith(prefix, StringComparison.Ordinal) ? item.FieldPath[prefix.Length..] : item.FieldPath;
            yield return string.Join('.', path.Split('.', StringSplitOptions.RemoveEmptyEntries).Select(segment => segment.Pascalize()));
        }
    }

    private static IQueryable<TEntity> Include<TEntity>(IQueryable<TEntity> query, string navigation)
        where TEntity : class {
        var method = EfInclude.Value;
        if (method is null) {
            return query;
        }

        return (IQueryable<TEntity>)method.MakeGenericMethod(typeof(TEntity)).Invoke(null, [query, navigation])!;
    }

    private static MethodInfo? ResolveEfInclude() {
        var type = Type.GetType(
            "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions, Microsoft.EntityFrameworkCore");
        if (type is null) {
            return null;
        }

        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static)) {
            if (method.Name != "Include" || !method.IsGenericMethodDefinition) {
                continue;
            }

            var parameters = method.GetParameters();
            if (parameters.Length == 2 && parameters[1].ParameterType == typeof(string)) {
                return method;
            }
        }

        return null;
    }

    private static PlanShape Lower(PlanNode root) {
        var filters = ImmutableArray.CreateBuilder<FilterNode>();
        OrderNode? order = null;
        var items = ImmutableArray<SelectionItem>.Empty;

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
                    throw new InsightValidationException(InsightReasons.Unimplemented, $"Plan node '{node.GetType().Name}' is not supported by the repository driver.");
            }
        }

        Visit(root);
        return new(filters.ToImmutable(), order, items);
    }

    private static async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> Rows<TEntity>(
        IAsyncEnumerable<TEntity>          entities,
        IReadOnlyList<Func<TEntity, bool>> residuals,
        ImmutableArray<SelectionItem>      items,
        string                             alias,
        [EnumeratorCancellation] CancellationToken ct
    ) where TEntity : class {
        await foreach (var entity in entities.WithCancellation(ct)) {
            if (residuals.All(residual => residual(entity))) {
                yield return RowMaterializer.ToRow(entity, items, alias);
            }
        }
    }

    private sealed record PlanShape(
        ImmutableArray<FilterNode>    Filters,
        OrderNode?                    Order,
        ImmutableArray<SelectionItem> Items);
}
