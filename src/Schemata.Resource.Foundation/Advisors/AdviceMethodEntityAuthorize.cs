using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Default order constants for <see cref="AdviceMethodEntityAuthorize{TEntity,TRequest,TResponse}" />.
/// </summary>
public static class AdviceMethodEntityAuthorize
{
    /// <summary>
    ///     Default order at <see cref="SchemataConstants.Orders.Base" />.
    /// </summary>
    public const int DefaultOrder = SchemataConstants.Orders.Base;
}

/// <summary>
///     Authorizes instance-scoped AIP-136 custom method invocations after the target entity loads.
///     Anonymous methods skip the access check. Other methods use the loaded entity for the AIP-211
///     primary check and parent-read probe.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TRequest">The custom-method request type.</typeparam>
/// <typeparam name="TResponse">The custom-method response type.</typeparam>
public sealed class AdviceMethodEntityAuthorize<TEntity, TRequest, TResponse>
    : IResourceMethodAdvisor<TEntity, TRequest, TResponse>
    where TEntity : class, ICanonicalName
    where TRequest : class
    where TResponse : class, ICanonicalName
{
    private readonly IAccessProvider<TEntity, TRequest> _access;

    /// <summary>
    ///     Initializes a new instance with the custom-method access provider.
    /// </summary>
    /// <param name="access">The custom-method access provider.</param>
    public AdviceMethodEntityAuthorize(IAccessProvider<TEntity, TRequest> access) { _access = access; }

    #region IResourceMethodAdvisor<TEntity,TRequest,TResponse> Members

    public int Order => AdviceMethodEntityAuthorize.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        TRequest          request,
        TEntity           entity,
        ClaimsPrincipal?  principal,
        CancellationToken ct = default
    ) {
        if (ctx.Has<AnonymousGranted>() || !ctx.TryGet<ResourceMethodVerb>(out var marker) || marker is null) {
            return AdviseResult.Continue;
        }

        var context = new AccessContext<TRequest> { Operation = marker.Verb, Request = request };
        await AuthorizeHelper.EnsureAsync(_access, entity, context, entity.CanonicalName ?? string.Empty, principal, ct);

        return AdviseResult.Continue;
    }

    #endregion
}
