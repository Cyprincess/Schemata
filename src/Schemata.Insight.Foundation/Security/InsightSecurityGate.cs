using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Insight.Skeleton.Queries;
using Schemata.Security.Skeleton;

namespace Schemata.Insight.Foundation.Security;

/// <summary>
///     Enforces source-level access and produces a row-level entitlement expression for a source's
///     row type. Drivers call this before streaming, so the entitlement is still pushed into the
///     backend query instead of degrading into a local filter.
/// </summary>
public static class InsightSecurityGate
{
    private const string Operation = "Insight";

    /// <summary>
    ///     Checks source-level access (throwing on denial) and returns the row-level entitlement
    ///     predicate, or <see langword="null" /> when no entitlement provider is registered.
    /// </summary>
    /// <typeparam name="TEntity">The source's row type.</typeparam>
    /// <param name="request">The query request (security context).</param>
    /// <param name="principal">The caller principal.</param>
    /// <param name="services">The provider resolving the Security providers.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The row-level entitlement predicate, or <see langword="null" />.</returns>
    /// <exception cref="PermissionDeniedException">Source-level access is denied.</exception>
    public static async Task<Expression<Func<TEntity, bool>>?> AuthorizeAsync<TEntity>(
        QueryInsightRequest request,
        ClaimsPrincipal?    principal,
        IServiceProvider    services,
        CancellationToken   ct
    )
        where TEntity : class {
        var context = new AccessContext<QueryInsightRequest> { Operation = Operation, Request = request };

        var access = services.GetService<IAccessProvider<TEntity, QueryInsightRequest>>();
        if (access is not null && !await access.HasAccessAsync(null, context, principal, ct)) {
            throw new PermissionDeniedException(
                SchemataResources.INSIGHT_ACCESS_DENIED,
                new Dictionary<string, string?> { ["name"] = typeof(TEntity).Name });
        }

        var entitlement = services.GetService<IEntitlementProvider<TEntity, QueryInsightRequest>>();
        if (entitlement is null) {
            return null;
        }

        return await entitlement.GenerateEntitlementExpressionAsync(context, principal, ct);
    }
}
