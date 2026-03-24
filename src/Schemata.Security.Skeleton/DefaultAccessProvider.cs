using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Security.Skeleton;

/// <summary>
///     Default access provider that grants access to all entities unconditionally.
/// </summary>
/// <typeparam name="T">The entity type.</typeparam>
/// <typeparam name="TContext">The context type.</typeparam>
/// <remarks>
///     This is the fallback registered by <see cref="Schemata.Security.Skeleton"/> when no custom
///     <see cref="IAccessProvider{T, TContext}"/> is configured. Replace it to enforce access control.
/// </remarks>
public class DefaultAccessProvider<T, TContext> : IAccessProvider<T, TContext>
{
    #region IAccessProvider<T,TContext> Members

    /// <inheritdoc />
    public Task<bool> HasAccessAsync(
        T?                entity,
        TContext?         context,
        ClaimsPrincipal?  principal,
        CancellationToken ct = default
    ) {
        return Task.FromResult(true);
    }

    #endregion
}
