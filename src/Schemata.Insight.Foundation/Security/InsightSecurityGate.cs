using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Insight.Skeleton;
using Schemata.Security.Skeleton;

namespace Schemata.Insight.Foundation;

/// <summary>
///     Enforces source-level access and produces a row-level entitlement expression for a source's
///     row type. The row type is known only at runtime, so the generic Security providers are closed
///     reflectively. Drivers call this before streaming.
/// </summary>
public static class InsightSecurityGate
{
    private const string Operation = "Insight";

    /// <summary>
    ///     Checks source-level access (throwing on denial) and returns the row-level entitlement
    ///     predicate, or null when no entitlement provider is registered.
    /// </summary>
    /// <param name="rowType">The source's row type.</param>
    /// <param name="request">The query request (security context).</param>
    /// <param name="principal">The caller principal.</param>
    /// <param name="services">The provider resolving the closed Security providers.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>An <c>Expression&lt;Func&lt;rowType, bool&gt;&gt;</c> entitlement, or null.</returns>
    /// <exception cref="PermissionDeniedException">Source-level access is denied.</exception>
    public static async Task<Expression?> AuthorizeAsync(
        Type                rowType,
        QueryInsightRequest request,
        ClaimsPrincipal?    principal,
        IServiceProvider    services,
        CancellationToken   ct
    ) {
        var context = new AccessContext<QueryInsightRequest> { Operation = Operation, Request = request };

        var accessType     = typeof(IAccessProvider<,>).MakeGenericType(rowType, typeof(QueryInsightRequest));
        var accessProvider = services.GetService(accessType);
        if (accessProvider is not null) {
            var allowed = (bool)(await InvokeAsync(accessType, accessProvider, "HasAccessAsync",
                                                   [null, context, principal, ct]))!;
            if (!allowed) {
                throw new PermissionDeniedException(
                    SchemataResources.INSIGHT_ACCESS_DENIED,
                    new Dictionary<string, string?> { ["name"] = rowType.Name });
            }
        }

        var entitlementType     = typeof(IEntitlementProvider<,>).MakeGenericType(rowType, typeof(QueryInsightRequest));
        var entitlementProvider = services.GetService(entitlementType);
        if (entitlementProvider is null) {
            return null;
        }

        return (Expression?)await InvokeAsync(entitlementType, entitlementProvider,
                                              "GenerateEntitlementExpressionAsync", [context, principal, ct]);
    }

    private static async Task<object?> InvokeAsync(Type contract, object provider, string method, object?[] args) {
        var task = (Task)contract.GetMethod(method)!.Invoke(provider, args)!;
        await task;
        return task.GetType().GetProperty("Result")!.GetValue(task);
    }
}
