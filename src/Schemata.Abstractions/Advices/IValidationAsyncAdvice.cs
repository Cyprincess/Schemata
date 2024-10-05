using System.Collections.Generic;
using Schemata.Abstractions.Entities;

namespace Schemata.Abstractions.Advices;

public interface IValidationAsyncAdvice<in T> : IAdvice<Operations, T, IList<KeyValuePair<string, string>>>;
