using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Security.Skeleton;

namespace Schemata.Security.Foundation;

/// <summary>
///     Composes IPermissionResolver and IPermissionMatcher into a claims-based access check.
///     A null principal is always denied.
/// </summary>
public sealed class DefaultAccessProvider<T, TRequest>(IPermissionResolver resolver, IPermissionMatcher matcher) : IAccessProvider<T, TRequest>
{
    #region IAccessProvider<T,TRequest> Members

    public Task<bool> HasAccessAsync(
        T?                      entity,
        AccessContext<TRequest> context,
        ClaimsPrincipal?        principal,
        CancellationToken       ct = default
    ) {
        if (principal is null) {
            return Task.FromResult(false);
        }

        if (string.IsNullOrWhiteSpace(context.Operation)) {
            return Task.FromResult(false);
        }

        var permission = resolver.Resolve(context.Operation, typeof(T));

        return Task.FromResult(matcher.IsMatch(principal, permission));
    }

    #endregion
}
