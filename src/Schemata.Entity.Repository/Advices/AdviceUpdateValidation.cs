using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advices;
using Schemata.Abstractions.Exceptions;

namespace Schemata.Entity.Repository.Advices;

public sealed class AdviceUpdateValidation<TEntity> : IRepositoryUpdateAsyncAdvice<TEntity>
    where TEntity : class
{
    private readonly IServiceProvider _services;

    public AdviceUpdateValidation(IServiceProvider services) {
        _services = services;
    }

    #region IRepositoryUpdateAsyncAdvice<TEntity> Members

    public int Order => Constants.Orders.Max;

    public int Priority => Order;

    public async Task<bool> AdviseAsync(IRepository<TEntity> repository, TEntity entity, CancellationToken ct) {
        var errors = new List<KeyValuePair<string, string>>();
        var pass   = await Advices<IValidationAsyncAdvice<TEntity>>.AdviseAsync(_services, Operations.Update, entity, errors, ct);
        if (pass) {
            return true;
        }

        throw new ValidationException(errors);
    }

    #endregion
}
