using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Schemata.Entity.Repository.Estimation;

namespace Schemata.Entity.EntityFrameworkCore;

internal sealed class EfCoreCountEstimator<TContext>(QueryEstimateProvider provider) : IEfCoreCountEstimator<TContext>
    where TContext : DbContext
{
    public async ValueTask<long?> EstimateAsync<TResult>(
        TContext context,
        IQueryable<TResult> query,
        CancellationToken ct = default
    ) {
        ct.ThrowIfCancellationRequested();
        if (context.Model.FindEntityType(typeof(TResult)) is null || !IsSupported(query.Expression)) {
            return null;
        }

        var matches = provider switch {
            QueryEstimateProvider.PostgreSql => context.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL",
            QueryEstimateProvider.MySql => context.Database.ProviderName is "Pomelo.EntityFrameworkCore.MySql"
                or "MySql.EntityFrameworkCore",
            QueryEstimateProvider.SqlServer => context.Database.ProviderName == "Microsoft.EntityFrameworkCore.SqlServer",
            _ => false,
        };
        if (!matches) {
            return null;
        }

        // A constant projection estimates roots rather than the multiplied rows of eager-loaded collections.
        await using var command = query.Cast<object>().IgnoreAutoIncludes().Select(_ => 1).CreateDbCommand();
        var opened = context.Database.GetDbConnection().State != ConnectionState.Open;
        if (opened) {
            await context.Database.OpenConnectionAsync(ct);
        }

        try {
            return await QueryPlanEstimate.EstimateAsync(command, provider, ct);
        } finally {
            if (opened) {
                await context.Database.CloseConnectionAsync();
            }
        }
    }

    private static bool IsSupported(Expression expression) {
        while (expression is MethodCallExpression call) {
            if (call.Method.DeclaringType == typeof(Queryable)) {
                switch (call.Method.Name) {
                    case nameof(Queryable.Where):
                    case nameof(Queryable.OrderBy):
                    case nameof(Queryable.OrderByDescending):
                    case nameof(Queryable.ThenBy):
                    case nameof(Queryable.ThenByDescending):
                    case nameof(Queryable.OfType):
                        break;
                    case nameof(Queryable.Cast):
                        var target = call.Method.GetGenericArguments()[0];
                        var source = call.Arguments[0].Type.GetGenericArguments()[0];
                        if (!target.IsAssignableFrom(source)) {
                            return false;
                        }
                        break;
                    default:
                        return false;
                }
            } else if (call.Method.DeclaringType == typeof(EntityFrameworkQueryableExtensions)) {
                switch (call.Method.Name) {
                    case nameof(EntityFrameworkQueryableExtensions.AsTracking):
                    case nameof(EntityFrameworkQueryableExtensions.AsNoTracking):
                    case nameof(EntityFrameworkQueryableExtensions.AsNoTrackingWithIdentityResolution):
                    case nameof(EntityFrameworkQueryableExtensions.TagWith):
                    case nameof(EntityFrameworkQueryableExtensions.TagWithCallSite):
                    case nameof(EntityFrameworkQueryableExtensions.IgnoreAutoIncludes):
                        break;
                    default:
                        return false;
                }
            } else {
                return false;
            }
            expression = call.Arguments[0];
        }

        // Derived roots include raw SQL and provider-specific operations whose cardinality is not established here.
        return expression.GetType() == typeof(EntityQueryRootExpression);
    }
}
