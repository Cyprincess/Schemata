using System.Collections.Generic;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Errors;

namespace Schemata.Validation.Skeleton.Advisors;

public interface IValidationAdvisor<in T> : IAdvisor<Operations, T, IList<ErrorFieldViolation>>;
