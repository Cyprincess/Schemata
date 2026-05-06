using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Default order constants for <see cref="AdviceCreateRequestValidation{TEntity,TRequest}" />.
/// </summary>
public static class AdviceCreateRequestValidation
{
    /// <summary>
    ///     Default order: runs after <see cref="AdviceCreateRequestAuthorize{TEntity,TRequest}" />.
    /// </summary>
    public const int DefaultOrder = AdviceCreateRequestAuthorize.DefaultOrder + 10_000_000;
}

/// <summary>
///     Validates create requests
///     per <seealso href="https://google.aip.dev/133">AIP-133: Standard methods: Create</seealso> by delegating to all
///     registered <c>IValidationAdvisor&lt;TRequest&gt;</c> implementations.
///     When the request has <c>ValidateOnly = true</c>, throws
///     <c>NoContentException</c> after validation to signal a dry-run.
///     Suppressed when <see cref="CreateRequestValidationSuppressed" /> is present
///     or <see cref="SchemataResourceOptions.SuppressCreateValidation" /> is set.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TRequest">The request DTO type.</typeparam>
public sealed class AdviceCreateRequestValidation<TEntity, TRequest> : IResourceCreateRequestAdvisor<TEntity, TRequest>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
{
    #region IResourceCreateRequestAdvisor<TEntity,TRequest> Members

    public int Order => AdviceCreateRequestValidation.DefaultOrder;

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
