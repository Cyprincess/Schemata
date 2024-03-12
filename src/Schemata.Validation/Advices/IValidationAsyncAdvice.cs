using System.Collections.Generic;
using FluentValidation;
using Schemata.Abstractions;

namespace Schemata.Validation.Advices;

public interface IValidationAsyncAdvice<TRequest> : IAdvice<IValidator<TRequest>, TRequest, IList<KeyValuePair<string, string>>>;
