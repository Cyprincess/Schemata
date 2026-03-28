using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using Humanizer;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Errors;
using Schemata.Validation.Skeleton.Advisors;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Validation.FluentValidation.Advisors;

public static class AdviceValidation
{
    public const int DefaultOrder = Orders.Base;
}

/// <summary>
///     Validation advisor that integrates FluentValidation into the Schemata validation pipeline.
/// </summary>
/// <typeparam name="T">The type being validated.</typeparam>
/// <remarks>
///     Resolves <see cref="IValidator{T}" /> from the service provider and runs validation,
///     translating FluentValidation failures into <see cref="ErrorFieldViolation" /> entries.
///     Auto-registered when <see cref="ServiceCollectionExtensions.AddValidator{TValidator}" /> is called.
/// </remarks>
public sealed class AdviceValidation<T> : IValidationAdvisor<T>
{
    private readonly IServiceProvider _sp;

    /// <summary>
    ///     Initializes a new instance with the specified service provider.
    /// </summary>
    /// <param name="sp">The service provider for resolving validators.</param>
    public AdviceValidation(IServiceProvider sp) { _sp = sp; }

    #region IValidationAdvisor<T> Members

    /// <inheritdoc />
    public int Order => AdviceValidation.DefaultOrder;

    /// <inheritdoc />
    public async Task<AdviseResult> AdviseAsync(
        AdviceContext              ctx,
        Operations                 operation,
        T                          request,
        IList<ErrorFieldViolation> errors,
        CancellationToken          ct = default
    ) {
        var validator = _sp.GetService<IValidator<T>>();
        if (validator is null) {
            return AdviseResult.Continue;
        }

        var context = new ValidationContext<T>(request, null,
                                               ValidatorOptions.Global.ValidatorSelectors
                                                               .DefaultValidatorSelectorFactory()) {
            RootContextData = { [nameof(Operations)] = operation },
        };

        var results = await validator.ValidateAsync(context, ct);
        if (results.IsValid || results.Errors.Count == 0) {
            return AdviseResult.Continue;
        }

        foreach (var error in results.Errors) {
            var field = error.PropertyName.Underscore();

            var code = error.ErrorCode.EndsWith("Validator")
                ? error.ErrorCode[..^9].Underscore()
                : error.ErrorCode.Underscore();
            var values = error.FormattedMessagePlaceholderValues;
            if (values.TryGetValue("ComparisonValue", out var c)) {
                code += $",{c}";
            } else if (values.TryGetValue("From", out var from)) {
                code += $",{from},{values["To"]}";
            } else if (values.TryGetValue("ExpectedPrecision", out var expected)) {
                code += $",{expected},{values["ExpectedScale"]}";
            } else {
                if (values.TryGetValue("MinLength", out var l)) {
                    code += $",{l}";
                }

                if (values.TryGetValue("MaxLength", out var u) && !u.Equals(l)) {
                    code += $",{u}";
                }
            }

            errors.Add(new() {
                Field       = field,
                Reason      = code,
                Description = error.ErrorMessage,
            });
        }

        return AdviseResult.Continue;
    }

    #endregion
}
