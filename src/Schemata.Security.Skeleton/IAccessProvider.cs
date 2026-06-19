using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Security.Skeleton;

/// <summary>Evaluates whether a principal can perform an operation on an entity.</summary>
/// <typeparam name="T">Entity type being authorized.</typeparam>
/// <typeparam name="TRequest">Request payload type used by the operation.</typeparam>
public interface IAccessProvider<T, TRequest>
{
    /// <summary>Returns whether the principal can access the entity for the requested operation.</summary>
    /// <param name="entity">Entity instance being authorized.</param>
    /// <param name="context">Operation and request details.</param>
    /// <param name="principal">Principal requesting access.</param>
    /// <param name="ct">Token that cancels the authorization check.</param>
    /// <returns><see langword="true"/> when access is allowed; otherwise, <see langword="false"/>.</returns>
    Task<bool> HasAccessAsync(
        T?                      entity,
        AccessContext<TRequest> context,
        ClaimsPrincipal?        principal,
        CancellationToken       ct = default
    );
}
