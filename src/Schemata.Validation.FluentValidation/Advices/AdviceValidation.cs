using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using Humanizer;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advices;
using Schemata.Abstractions.Entities;

namespace Schemata.Validation.FluentValidation.Advices;

public sealed class AdviceValidation<T>(IServiceProvider sp) : IValidationAsyncAdvice<T>
{
    #region IValidationAsyncAdvice<T> Members

    public int Order => 100_000_000;

    public int Priority => Order;

    public async Task<bool> AdviseAsync(
        AdviceContext                       ctx,
        Operations                          operation,
        T                                   request,
        IList<KeyValuePair<string, string>> errors,
        CancellationToken                   ct = default) {
        var validator = sp.GetService<IValidator<T>>();
        if (validator is null) {
            return true;
        }

        var context = new ValidationContext<T>(request,
            null,
            ValidatorOptions.Global.ValidatorSelectors.DefaultValidatorSelectorFactory()) {
            RootContextData = { [nameof(Operations)] = operation },
        };

        var results = await validator.ValidateAsync(context, ct);
        if (results.IsValid || results.Errors.Count == 0) {
            return true;
        }

        foreach (var error in results.Errors) {
            var field = error.PropertyName.Underscore();

            var code = error.ErrorCode.EndsWith("Validator")
                ? error.ErrorCode.Substring(0, error.ErrorCode.Length - 9).Underscore()
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

            errors.Add(new(field, code));
        }

        return true;
    }

    #endregion
}
