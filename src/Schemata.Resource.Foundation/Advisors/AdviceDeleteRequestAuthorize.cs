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

public static class AdviceDeleteRequestAuthorize
{
    public const int DefaultOrder = SchemataConstants.Orders.Base;
}

/// <summary>
/// Authorizes delete requests by checking role-based access for the current user.
/// </summary>
/// <typeparam name="TEntity">The entity type being deleted.</typeparam>
/// <remarks>
/// Order: 100,000,000. Registered by <see cref="SchemataResourceBuilder.WithAuthorization"/>;
/// not auto-registered by <see cref="Features.SchemataResourceFeature"/>.
/// Skips authorization when the entity is decorated with <see cref="Schemata.Abstractions.Resource.AnonymousAttribute"/> for the Delete operation.
/// Throws <see cref="Schemata.Abstractions.Exceptions.AuthorizationException"/> if access is denied.
/// </remarks>
public sealed class AdviceDeleteRequestAuthorize<TEntity> : IResourceDeleteRequestAdvisor<TEntity>
    where TEntity : class, ICanonicalName
{
    private readonly IAccessProvider<TEntity, ResourceRequestContext<DeleteRequest>> _access;

    public AdviceDeleteRequestAuthorize(IAccessProvider<TEntity, ResourceRequestContext<DeleteRequest>> access) {
        _access = access;
    }

    #region IResourceDeleteRequestAdvisor<TEntity> Members

    /// <inheritdoc />
    public int Order => AdviceDeleteRequestAuthorize.DefaultOrder;

    /// <inheritdoc />
    public async Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        DeleteRequest     request,
        HttpContext?      http,
        CancellationToken ct = default
    ) {
        if (AnonymousAccessHelper.IsAnonymous<TEntity>(Operations.Delete)) {
            return AdviseResult.Continue;
        }

        var result = await _access.HasAccessAsync(null, new() { Operation = Operations.Delete, Request = request }, http?.User, ct);

        if (!result) {
            throw new AuthorizationException();
        }

        return AdviseResult.Continue;
    }

    #endregion
}
