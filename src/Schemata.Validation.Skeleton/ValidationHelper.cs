using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Advice;
using Schemata.Validation.Skeleton.Advisors;

namespace Schemata.Validation.Skeleton;

/// <summary>
///     Runs the registered validation advisors for one operation and throws the collected field violations.
/// </summary>
public static class ValidationHelper
{
    /// <summary>
    ///     Runs all registered <see cref="IValidationAdvisor{TRequest}" /> implementations against the value.
    /// </summary>
    public static async Task<AdviseResult> ValidateAsync<TRequest>(
        AdviceContext     ctx,
        TRequest          request,
        Operations        operation,
        CancellationToken ct = default
    )
        where TRequest : class {
        var errors = new List<ErrorFieldViolation>();
        switch (await Advisor.For<IValidationAdvisor<TRequest>>()
                             .RunAsync(ctx, operation, request, errors, ct)) {
            case AdviseResult.Block:
                throw new ValidationException(errors);
            case AdviseResult.Handle:
            case AdviseResult.Continue:
            default:
                break;
        }

        return AdviseResult.Continue;
    }
}
