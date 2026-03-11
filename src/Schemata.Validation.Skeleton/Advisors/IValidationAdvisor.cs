using System.Collections.Generic;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Validation.Skeleton.Advisors;

public interface IValidationAdvisor<in T> : IAdvisor<Operations, T, IList<KeyValuePair<string, string>>>;
