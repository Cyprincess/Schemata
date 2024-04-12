using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advices;

namespace Schemata.Validation.FluentValidation.Advices;

public sealed class AdviceValidationErrors<T> : IValidationAsyncAdvice<T>
{
    #region IValidationAsyncAdvice<T> Members

    public int Order => Constants.Orders.Max;

    public int Priority => Order;

    public Task<bool> AdviseAsync(
        Operations                          operation,
        T                                   request,
        IList<KeyValuePair<string, string>> errors,
        CancellationToken                   ct = default) {
        return Task.FromResult(errors.Count == 0);
    }

    #endregion
}
