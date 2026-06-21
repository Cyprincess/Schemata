using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>
///     Shared permission gate for the per-flow authorization advisors. Each advisor stays a distinct
///     pipeline type but delegates the "check a permission, throw an OAuth error when absent" body here.
/// </summary>
public static class PermissionAdvice
{
    /// <summary>
    ///     Throws an <see cref="OAuthException" /> when <paramref name="application" /> lacks
    ///     <paramref name="permission" />.
    /// </summary>
    /// <typeparam name="TApp">The application entity type.</typeparam>
    /// <param name="manager">The application manager that evaluates the permission.</param>
    /// <param name="application">The application to check; may be <see langword="null" />.</param>
    /// <param name="permission">The fully-qualified permission entry (prefix + value).</param>
    /// <param name="ct">A cancellation token.</param>
    /// <param name="error">The OAuth error code; defaults to <c>unauthorized_client</c>.</param>
    /// <param name="resource">The resource string key for the error description; defaults to <c>ST4007</c>.</param>
    /// <param name="code">The HTTP status code; defaults to 400.</param>
    /// <param name="configure">Optional hook to enrich the exception (e.g. redirect parameters) before it is thrown.</param>
    public static async Task RequireAsync<TApp>(
        IApplicationManager<TApp> manager,
        TApp?                     application,
        string                    permission,
        CancellationToken         ct,
        string?                   error     = null,
        string?                   resource  = null,
        int                       code      = 400,
        Action<OAuthException>?   configure = null
    )
        where TApp : SchemataApplication {
        if (await manager.HasPermissionAsync(application, permission, ct)) {
            return;
        }

        var exception = new OAuthException(
            error ?? OAuthErrors.UnauthorizedClient,
            SchemataResources.GetResourceString(resource ?? SchemataResources.ST4007),
            code);
        configure?.Invoke(exception);
        throw exception;
    }
}
