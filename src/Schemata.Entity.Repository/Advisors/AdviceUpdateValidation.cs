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

/// <summary>Order constants for <see cref="AdviceUpdateValidation{TEntity}" />.</summary>
public static class AdviceUpdateValidation
{
    /// <summary>
    ///     Default execution order: after <see cref="AdviceUpdateTimestamp{TEntity}" />
    ///     (100,000,000 + 10,000,000 = 110,000,000).
    /// </summary>
    public const int DefaultOrder = AdviceUpdateTimestamp.DefaultOrder + 10_000_000;
}

/// <summary>
///     Runs registered <see cref="IValidationAdvisor{TEntity}" /> advisors against the entity
///     before it is updated. Throws <see cref="ValidationException" /> if any advisor returns
///     <see cref="AdviseResult.Block" />.
/// </summary>
/// <typeparam name="TEntity">The entity type being updated.</typeparam>
/// <remarks>
///     Suppressed by <see cref="UpdateValidationSuppressed" />.
/// </remarks>
public sealed class AdviceUpdateValidation<TEntity> : IRepositoryUpdateAdvisor<TEntity>
    where TEntity : class
{
    #region IRepositoryUpdateAdvisor<TEntity> Members

    /// <inheritdoc />
    public int Order => AdviceUpdateValidation.DefaultOrder;

    /// <inheritdoc />
    public async Task<AdviseResult> AdviseAsync(
        AdviceContext        ctx,
        IRepository<TEntity> repository,
        TEntity              entity,
        CancellationToken    ct
    ) {
        if (ctx.Has<UpdateValidationSuppressed>()) {
            return AdviseResult.Continue;
        }

        var errors = new List<ErrorFieldViolation>();
        switch (await Advisor.For<IValidationAdvisor<TEntity>>()
                             .RunAsync(ctx, Operations.Update, entity, errors, ct)) {
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
