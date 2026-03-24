using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Advice;
using Schemata.Validation.Skeleton.Advisors;

namespace Schemata.Entity.Repository.Advisors;

/// <summary>Order constants for <see cref="AdviceUpdateValidation{TEntity}"/>.</summary>
public static class AdviceUpdateValidation
{
    /// <summary>Default execution order.</summary>
    public const int DefaultOrder = AdviceUpdateTimestamp.DefaultOrder + 10_000_000;
}

/// <summary>
///     Runs registered validation advisors against the entity before it is updated.
/// </summary>
/// <typeparam name="TEntity">The entity type being updated.</typeparam>
/// <remarks>
///     <para>Order: <see cref="SchemataConstants.Orders.Max" /> (2,147,400,000). Runs last in the update pipeline.</para>
///     <para>Auto-registered by <see cref="Microsoft.Extensions.DependencyInjection.ServiceCollectionExtensions.AddRepository" />.</para>
///     <para>Throws <see cref="ValidationException" /> when validation fails with <see cref="AdviseResult.Block" />.</para>
///     <para>Suppressed when <see cref="SuppressUpdateValidation" /> is present in the advice context.</para>
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
        if (ctx.Has<SuppressUpdateValidation>()) {
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
