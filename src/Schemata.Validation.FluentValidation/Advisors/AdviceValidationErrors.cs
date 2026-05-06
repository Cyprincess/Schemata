using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Errors;
using Schemata.Validation.Skeleton.Advisors;

namespace Schemata.Validation.FluentValidation.Advisors;

/// <summary>
///     Order constants for <see cref="AdviceValidationErrors{T}" />.
/// </summary>
public static class AdviceValidationErrors
{
    /// <summary>
    ///     The default execution order for this advisor, running after <see cref="AdviceValidation.DefaultOrder" />.
    /// </summary>
    public const int DefaultOrder = AdviceValidation.DefaultOrder + 10_000_000;
}

/// <summary>
///     Terminal validation advisor that blocks the pipeline when validation errors have been accumulated.
/// </summary>
/// <typeparam name="T">The type being validated.</typeparam>
/// <remarks>
///     Runs at <see cref="SchemataConstants.Orders.Max" /> (last in the pipeline) and returns
///     <see cref="AdviseResult.Block" /> if the errors list is non-empty, preventing further processing.
/// </remarks>
public sealed class AdviceValidationErrors<T> : IValidationAdvisor<T>
{
    #region IValidationAdvisor<T> Members

    public int Order => AdviceValidationErrors.DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext              ctx,
        Operations                 operation,
        T                          request,
        IList<ErrorFieldViolation> errors,
        CancellationToken          ct = default
    ) {
        return Task.FromResult(errors.Count == 0 ? AdviseResult.Continue : AdviseResult.Block);
    }

    #endregion
}
