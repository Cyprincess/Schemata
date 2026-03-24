using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

public static class AdviceUpdateRequestAuthorize
{
    public const int DefaultOrder = SchemataConstants.Orders.Base;
}

/// <summary>
/// Authorizes update requests by checking role-based access for the current user.
/// </summary>
/// <typeparam name="TEntity">The entity type being updated.</typeparam>
/// <typeparam name="TRequest">The request DTO type carrying update data.</typeparam>
/// <remarks>
/// Order: 100,000,000. Registered by <see cref="SchemataResourceBuilder.WithAuthorization"/>;
/// not auto-registered by <see cref="Features.SchemataResourceFeature"/>.
/// Skips authorization when the entity is decorated with <see cref="Schemata.Abstractions.Resource.AnonymousAttribute"/> for the Update operation.
/// Throws <see cref="Schemata.Abstractions.Exceptions.AuthorizationException"/> if access is denied.
/// </remarks>
public sealed class AdviceUpdateRequestAuthorize<TEntity, TRequest> : IResourceUpdateRequestAdvisor<TEntity, TRequest>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
{
    private readonly IAccessProvider<TEntity, ResourceRequestContext<TRequest>> _access;

    public AdviceUpdateRequestAuthorize(IAccessProvider<TEntity, ResourceRequestContext<TRequest>> access) {
        _access = access;
    }

    #region IResourceUpdateRequestAdvisor<TEntity,TRequest> Members

    /// <inheritdoc />
    public int Order => AdviceUpdateRequestAuthorize.DefaultOrder;

    /// <inheritdoc />
    public async Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        TRequest          request,
        HttpContext?      http,
        CancellationToken ct = default
    ) {
        if (AnonymousAccessHelper.IsAnonymous<TEntity>(Operations.Update)) {
            return AdviseResult.Continue;
        }

        var result = await _access.HasAccessAsync(null, new() { Operation = Operations.Update, Request = request }, http?.User, ct);

        if (!result) {
            throw new AuthorizationException();
        }

        return AdviseResult.Continue;
    }

    #endregion
}
