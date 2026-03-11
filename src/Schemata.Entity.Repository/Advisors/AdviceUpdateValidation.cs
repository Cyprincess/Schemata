using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Advice;
using Schemata.Validation.Skeleton.Advisors;

namespace Schemata.Entity.Repository.Advisors;

public sealed class AdviceUpdateValidation<TEntity> : IRepositoryUpdateAdvisor<TEntity>
    where TEntity : class
{
    #region IRepositoryUpdateAdvisor<TEntity> Members

    public int Order => SchemataConstants.Orders.Max;

    public int Priority => Order;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext        ctx,
        IRepository<TEntity> repository,
        TEntity              entity,
        CancellationToken    ct
    ) {
        if (ctx.Has<SuppressUpdateValidation>()) {
            return AdviseResult.Continue;
        }

        var errors = new List<KeyValuePair<string, string>>();
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
