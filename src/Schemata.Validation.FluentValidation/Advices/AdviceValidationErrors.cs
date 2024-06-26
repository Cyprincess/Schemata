using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advices;
using Schemata.Abstractions.Entities;

namespace Schemata.Validation.FluentValidation.Advices;

public sealed class AdviceValidationErrors<T> : IValidationAsyncAdvice<T>
{
    #region IValidationAsyncAdvice<T> Members

    public int Order => SchemataConstants.Orders.Max;

    public int Priority => Order;

    public Task<bool> AdviseAsync(
        AdviceContext                       ctx,
        Operations                          operation,
        T                                   request,
        IList<KeyValuePair<string, string>> errors,
        CancellationToken                   ct = default) {
        return Task.FromResult(errors.Count == 0);
    }

    #endregion
}
