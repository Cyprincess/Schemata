using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Security.Skeleton;

namespace Schemata.Security.Foundation;

/// <summary>
///     Composes IPermissionResolver and IPermissionMatcher into a claims-based access check.
///     Anonymous principals are definitely denied. The coarse permission match decides
///     <see cref="AccessDecision.Allowed" /> / <see cref="AccessDecision.Denied" /> whenever the
///     operation and an authenticated principal are present — including the
///     <see cref="AccessStage.Missing" /> disclosure stage, where a matched operation permission
///     proves the caller may learn the target is absent. A missing operation or an absent
///     principal-to-permission mapping the resolver cannot produce leaves the decision
///     <see cref="AccessDecision.Indeterminate" />, which callers fail closed on. The provider
///     never fabricates a parent read-children check from a child operation permission —
///     parent-aware disclosure policy belongs to a custom provider.
/// </summary>
public sealed class DefaultAccessProvider<T, TRequest>(IPermissionResolver resolver, IPermissionMatcher matcher) : IAccessProvider<T, TRequest>
{
    #region IAccessProvider<T,TRequest> Members

    public Task<AccessDecision> HasAccessAsync(
        T?                      entity,
        AccessContext<TRequest> context,
        ClaimsPrincipal?        principal,
        CancellationToken       ct = default
    ) {
        if (principal?.Identity?.IsAuthenticated != true) {
            return Task.FromResult(AccessDecision.Denied);
        }

        if (string.IsNullOrWhiteSpace(context.Operation)) {
            return Task.FromResult(AccessDecision.Indeterminate);
        }

        var permission = resolver.Resolve(context.Operation, typeof(T));

        return Task.FromResult(matcher.IsMatch(principal, permission)
            ? AccessDecision.Allowed
            : AccessDecision.Denied);
    }

    #endregion
}
