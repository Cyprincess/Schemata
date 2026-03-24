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

/// <summary>Order constants for <see cref="AdviceAddValidation{TEntity}"/>.</summary>
public static class AdviceAddValidation
{
    /// <summary>Default execution order.</summary>
    public const int DefaultOrder = AdviceAddCanonicalName.DefaultOrder + 10_000_000;
}

/// <summary>
///     Runs registered validation advisors against the entity before it is added.
/// </summary>
/// <typeparam name="TEntity">The entity type being added.</typeparam>
/// <remarks>
///     <para>Order: <see cref="SchemataConstants.Orders.Max" /> (2,147,400,000). Runs last in the add pipeline.</para>
///     <para>Auto-registered by <see cref="Microsoft.Extensions.DependencyInjection.ServiceCollectionExtensions.AddRepository" />.</para>
///     <para>Throws <see cref="ValidationException" /> when validation fails with <see cref="AdviseResult.Block" />.</para>
///     <para>Suppressed when <see cref="SuppressAddValidation" /> is present in the advice context.</para>
/// </remarks>
public sealed class AdviceAddValidation<TEntity> : IRepositoryAddAdvisor<TEntity>
    where TEntity : class
{
    #region IRepositoryAddAdvisor<TEntity> Members

    /// <inheritdoc />
    public int Order => AdviceAddValidation.DefaultOrder;

    /// <inheritdoc />
    public async Task<AdviseResult> AdviseAsync(
        AdviceContext        ctx,
        IRepository<TEntity> repository,
        TEntity              entity,
        CancellationToken    ct
    ) {
        if (ctx.Has<SuppressAddValidation>()) {
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
