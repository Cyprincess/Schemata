using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Security.Skeleton;

/// <summary>
///     Determines whether a principal has access to a specific entity within a given context.
/// </summary>
/// <typeparam name="T">The entity type to authorize access for.</typeparam>
/// <typeparam name="TContext">The context type that provides additional authorization information.</typeparam>
/// <remarks>
///     Implementations form the entity-level access control layer of the Schemata security model.
///     For each request, <see cref="HasAccessAsync"/> is evaluated to gate operations on individual entities.
///     Register custom implementations via <c>AddAccessProvider</c> to replace the default allow-all behavior.
/// </remarks>
public interface IAccessProvider<T, TContext>
{
    /// <summary>
    ///     Evaluates whether the specified principal has access to the given entity.
    /// </summary>
    /// <param name="entity">The entity being accessed, or <see langword="null"/> for collection-level checks.</param>
    /// <param name="context">The context providing additional authorization state.</param>
    /// <param name="principal">The claims principal representing the current user.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> if access is granted; otherwise, <see langword="false"/>.</returns>
    Task<bool> HasAccessAsync(
        T?                entity,
        TContext?         context,
        ClaimsPrincipal?  principal,
        CancellationToken ct = default
    );
}
