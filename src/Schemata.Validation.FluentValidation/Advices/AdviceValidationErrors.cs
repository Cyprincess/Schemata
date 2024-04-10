using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;

namespace Schemata.Validation.FluentValidation.Advices;

public class AdviceValidationErrors<TRequest> : IValidationAsyncAdvice<TRequest>
{
    #region IValidationAsyncAdvice<TRequest> Members

    public int Order => 2_147_400_000;

    public int Priority => Order;

    public Task<bool> AdviseAsync(
        IValidator<TRequest>                validator,
        TRequest                            request,
        IList<KeyValuePair<string, string>> errors,
        CancellationToken                   ct = default) {
        return Task.FromResult(errors.Count == 0);
    }

    #endregion
}
