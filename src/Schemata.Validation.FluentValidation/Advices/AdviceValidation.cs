using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using Humanizer;
using Schemata.Abstractions;
using Schemata.Abstractions.Advices;

namespace Schemata.Validation.FluentValidation.Advices;

public sealed class AdviceValidation<T> : IValidationAsyncAdvice<T>
{
    private readonly IValidator<T> _validator;

    public AdviceValidation(IValidator<T> validator) {
        _validator = validator;
    }

    #region IValidationAsyncAdvice<T> Members

    public int Order => 1_000_000_000;

    public int Priority => Order;

    public async Task<bool> AdviseAsync(
        Operations                          operation,
        T                                   request,
        IList<KeyValuePair<string, string>> errors,
        CancellationToken                   ct = default) {
        var context = new ValidationContext<T>(request, null, ValidatorOptions.Global.ValidatorSelectors.DefaultValidatorSelectorFactory());
        context.RootContextData[nameof(Operations)] = operation;

        var results = await _validator.ValidateAsync(context, ct);
        if (results.IsValid || results.Errors.Count == 0) {
            return true;
        }

        foreach (var error in results.Errors) {
            var field  = error.PropertyName.Underscore();
            var code   = error.ErrorCode[..^9].Underscore();
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
