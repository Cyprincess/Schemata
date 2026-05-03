using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Advice;
using Schemata.Validation.Skeleton.Advisors;

namespace Schemata.Resource.Foundation.Advisors;

internal static class ValidationHelper
{
    /// <summary>
    ///     Runs all registered <see cref="IValidationAdvisor{TRequest}" /> implementations against the request.
    ///     Supports <c>ValidateOnly</c> dry-runs and per-operation suppression via the
    ///     <paramref name="suppressed" /> flag. On validation failure, throws
    ///     <see cref="ValidationException" /> with the collected errors.
    /// </summary>
    public static async Task<AdviseResult> ValidateAsync<TRequest>(
        AdviceContext     ctx,
        TRequest          request,
        Operations        operation,
        bool              suppressed,
        CancellationToken ct = default
    )
        where TRequest : class {
        var only = request is IValidation { ValidateOnly: true };

        if (suppressed) {
            if (only) {
                throw new NoContentException();
            }

            return AdviseResult.Continue;
        }

        var errors = new List<ErrorFieldViolation>();
        switch (await Advisor.For<IValidationAdvisor<TRequest>>()
                             .RunAsync(ctx, operation, request, errors, ct)) {
            case AdviseResult.Block:
                throw new ValidationException(errors);
            case AdviseResult.Handle:
                return AdviseResult.Handle;
            case AdviseResult.Continue:
            default:
                break;
        }

        if (only) {
            throw new NoContentException();
        }

        return AdviseResult.Continue;
    }
}
