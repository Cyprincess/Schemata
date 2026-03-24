using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

public static class AdviceCreateRequestAuthorize
{
    public const int DefaultOrder = AdviceCreateRequestIdempotency.DefaultOrder + 10_000_000;
}

/// <summary>
/// Authorizes create requests by checking role-based access for the current user.
/// </summary>
/// <typeparam name="TEntity">The entity type being created.</typeparam>
/// <typeparam name="TRequest">The request DTO type carrying creation data.</typeparam>
/// <remarks>
/// Order: 100,000,000. Registered by <see cref="SchemataResourceBuilder.WithAuthorization"/>;
/// not auto-registered by <see cref="Features.SchemataResourceFeature"/>.
/// Skips authorization when the entity is decorated with <see cref="Schemata.Abstractions.Resource.AnonymousAttribute"/> for the Create operation.
/// Throws <see cref="Schemata.Abstractions.Exceptions.AuthorizationException"/> if access is denied.
/// </remarks>
public sealed class AdviceCreateRequestAuthorize<TEntity, TRequest> : IResourceCreateRequestAdvisor<TEntity, TRequest>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
{
    private readonly IAccessProvider<TEntity, ResourceRequestContext<TRequest>> _access;

    public AdviceCreateRequestAuthorize(IAccessProvider<TEntity, ResourceRequestContext<TRequest>> access) {
        _access = access;
    }

    #region IResourceCreateRequestAdvisor<TEntity,TRequest> Members

    /// <inheritdoc />
    public int Order => AdviceCreateRequestAuthorize.DefaultOrder;

    /// <inheritdoc />
    public async Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        TRequest          request,
        HttpContext?      http,
        CancellationToken ct = default
    ) {
        if (AnonymousAccessHelper.IsAnonymous<TEntity>(Operations.Create)) {
            return AdviseResult.Continue;
        }

        var result = await _access.HasAccessAsync(null, new() { Operation = Operations.Create, Request = request }, http?.User, ct);

        if (!result) {
            throw new AuthorizationException();
        }

        return AdviseResult.Continue;
    }

    #endregion
}
