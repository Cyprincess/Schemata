using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Advice;
using Schemata.Validation.Skeleton.Advisors;

namespace Schemata.Entity.Repository.Advisors;

/// <summary>Order constants for <see cref="AdviceAddValidation{TEntity}" />.</summary>
public static class AdviceAddValidation
{
    /// <summary>
    ///     Default execution order: after <see cref="AdviceAddCanonicalName{TEntity}" />
    ///     (220,000,000 + 10,000,000 = 230,000,000).
    /// </summary>
    public const int DefaultOrder = AdviceAddCanonicalName.DefaultOrder + 10_000_000;
}

/// <summary>
///     Runs registered <see cref="IValidationAdvisor{TEntity}" /> advisors against the entity
///     before it is added. Throws <see cref="ValidationException" /> if any advisor returns
///     <see cref="AdviseResult.Block" />.
/// </summary>
/// <typeparam name="TEntity">The entity type being added.</typeparam>
/// <remarks>
///     Suppressed by <see cref="AddValidationSuppressed" />.
/// </remarks>
public sealed class AdviceAddValidation<TEntity> : IRepositoryAddAdvisor<TEntity>
    where TEntity : class
{
    #region IRepositoryAddAdvisor<TEntity> Members

    public int Order => AdviceAddValidation.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext        ctx,
        IRepository<TEntity> repository,
        TEntity              entity,
        CancellationToken    ct
    ) {
        if (ctx.Has<AddValidationSuppressed>()) {
            return AdviseResult.Continue;
        }

        var errors = new List<ErrorFieldViolation>();
        switch (await Advisor.For<IValidationAdvisor<TEntity>>()
                             .RunAsync(ctx, Operations.Create, entity, errors, ct)) {
            case AdviseResult.Block:
                throw new ValidationException(errors);
            case AdviseResult.Handle:
                return AdviseResult.Handle;
            case AdviseResult.Continue:
            default:
                break;
        }

        return AdviseResult.Continue;
    }

    #endregion
}
