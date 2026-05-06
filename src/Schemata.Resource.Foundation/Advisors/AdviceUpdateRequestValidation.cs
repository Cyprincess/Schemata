using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Default order constants for <see cref="AdviceUpdateRequestValidation{TEntity,TRequest}" />.
/// </summary>
public static class AdviceUpdateRequestValidation
{
    /// <summary>
    ///     Default order: runs after <see cref="AdviceUpdateRequestAuthorize{TEntity,TRequest}" />.
    /// </summary>
    public const int DefaultOrder = AdviceUpdateRequestAuthorize.DefaultOrder + 10_000_000;
}

/// <summary>
///     Validates update requests
///     per <seealso href="https://google.aip.dev/134">AIP-134: Standard methods: Update</seealso> by delegating to all
///     registered <c>IValidationAdvisor&lt;TRequest&gt;</c> implementations.
///     When the request has <c>ValidateOnly = true</c>, throws
///     <c>NoContentException</c> after validation to signal a dry-run.
///     Suppressed when <see cref="UpdateRequestValidationSuppressed" /> is present
///     or <see cref="SchemataResourceOptions.SuppressUpdateValidation" /> is set.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TRequest">The request DTO type.</typeparam>
public sealed class AdviceUpdateRequestValidation<TEntity, TRequest> : IResourceUpdateRequestAdvisor<TEntity, TRequest>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
{
    #region IResourceUpdateRequestAdvisor<TEntity,TRequest> Members

    public int Order => AdviceUpdateRequestValidation.DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext                     ctx,
        TRequest                          request,
        ResourceRequestContainer<TEntity> container,
        ClaimsPrincipal?                  principal,
        CancellationToken                 ct = default
    ) {
        return ValidationHelper.ValidateAsync(ctx, request, Operations.Update, ctx.Has<UpdateRequestValidationSuppressed>(), ct);
    }

    #endregion
}
