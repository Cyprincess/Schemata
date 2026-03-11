using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Validation.Skeleton.Advisors;

namespace Schemata.Validation.FluentValidation.Advisors;

public sealed class AdviceValidationErrors<T> : IValidationAdvisor<T>
{
    #region IValidationAdvisor<T> Members

    public int Order => SchemataConstants.Orders.Max;

    public int Priority => Order;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext                       ctx,
        Operations                          operation,
        T                                   request,
        IList<KeyValuePair<string, string>> errors,
        CancellationToken                   ct = default
    ) {
        return Task.FromResult(errors.Count == 0 ? AdviseResult.Continue : AdviseResult.Block);
    }

    #endregion
}
