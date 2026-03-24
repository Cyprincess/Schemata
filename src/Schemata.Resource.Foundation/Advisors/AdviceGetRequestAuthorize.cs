using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

public static class AdviceGetRequestAuthorize
{
    public const int DefaultOrder = SchemataConstants.Orders.Base;
}

/// <summary>
/// Authorizes get requests by checking role-based access for the current user.
/// </summary>
/// <typeparam name="TEntity">The entity type being retrieved.</typeparam>
/// <remarks>
/// Order: 100,000,000. Registered by <see cref="SchemataResourceBuilder.WithAuthorization"/>;
/// not auto-registered by <see cref="Features.SchemataResourceFeature"/>.
/// Skips authorization when the entity is decorated with <see cref="Schemata.Abstractions.Resource.AnonymousAttribute"/> for the Get operation.
/// Throws <see cref="Schemata.Abstractions.Exceptions.AuthorizationException"/> if access is denied.
/// </remarks>
public sealed class AdviceGetRequestAuthorize<TEntity> : IResourceGetRequestAdvisor<TEntity>
    where TEntity : class, ICanonicalName
{
    private readonly IAccessProvider<TEntity, ResourceRequestContext<GetRequest>> _access;

    public AdviceGetRequestAuthorize(IAccessProvider<TEntity, ResourceRequestContext<GetRequest>> access) {
        _access = access;
    }

    #region IResourceGetRequestAdvisor<TEntity> Members

    /// <inheritdoc />
    public int Order => AdviceGetRequestAuthorize.DefaultOrder;

    /// <inheritdoc />
    public async Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        GetRequest        request,
        HttpContext?      http,
        CancellationToken ct = default
    ) {
        if (AnonymousAccessHelper.IsAnonymous<TEntity>(Operations.Get)) {
            return AdviseResult.Continue;
        }

        var result = await _access.HasAccessAsync(null, new() { Operation = Operations.Get, Request = request }, http?.User, ct);

        if (!result) {
            throw new AuthorizationException();
        }

        return AdviseResult.Continue;
    }

    #endregion
}
