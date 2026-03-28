using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advisors;

public static class AdviceCreateRequestValidation
{
    public const int DefaultOrder = AdviceCreateRequestAuthorize.DefaultOrder + 10_000_000;
}

/// <summary>
///     Validates create requests using registered validation advisors.
/// </summary>
/// <typeparam name="TEntity">The entity type being created.</typeparam>
/// <typeparam name="TRequest">The request DTO type to validate.</typeparam>
/// <remarks>
///     Order: 200,000,000. Auto-registered by <see cref="Features.SchemataResourceFeature" />.
///     Delegates to <c>IValidationAdvisor&lt;TRequest&gt;</c> implementations.
///     When the request has <see cref="Schemata.Abstractions.Resource.IValidation.ValidateOnly" /> =
///     <see langword="true" />, throws <see cref="Schemata.Abstractions.Exceptions.NoContentException" /> after validation
///     to signal a dry-run. Suppressed when <see cref="CreateRequestValidationSuppressed" /> is present
///     in the advice context or when <see cref="SchemataResourceOptions.SuppressCreateValidation" /> is set.
/// </remarks>
public sealed class AdviceCreateRequestValidation<TEntity, TRequest> : IResourceCreateRequestAdvisor<TEntity, TRequest>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
{
    #region IResourceCreateRequestAdvisor<TEntity,TRequest> Members

    /// <inheritdoc />
    public int Order => AdviceCreateRequestValidation.DefaultOrder;

    /// <inheritdoc />
    public Task<AdviseResult> AdviseAsync(
        AdviceContext                     ctx,
        TRequest                          request,
        ResourceRequestContainer<TEntity> container,
        ClaimsPrincipal?                  principal,
        CancellationToken                 ct = default
    ) {
        return ValidationHelper.ValidateAsync(ctx, request, Operations.Create, ctx.Has<CreateRequestValidationSuppressed>(), ct);
    }

    #endregion
}
