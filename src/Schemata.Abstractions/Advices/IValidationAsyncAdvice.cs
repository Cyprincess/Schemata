using System.Collections.Generic;

namespace Schemata.Abstractions.Advices;

public interface IValidationAsyncAdvice<T> : IAdvice<Operations, T, IList<KeyValuePair<string, string>>>;
