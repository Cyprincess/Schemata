using System.Collections.Generic;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Errors;

namespace Schemata.Validation.Skeleton.Advisors;

/// <summary>
///     Advisor interface for the validation pipeline.
/// </summary>
/// <typeparam name="T">The type being validated.</typeparam>
/// <remarks>
///     Implementations receive the CRUD <see cref="Operations" /> kind, the entity being validated,
///     and a mutable list of <see cref="ErrorFieldViolation" /> to populate with validation errors.
///     Multiple advisors run in <see cref="Schemata.Abstractions.IFeature.Order" /> sequence. The final advisor
///     (typically <c>AdviceValidationErrors</c>) blocks the pipeline if errors were accumulated.
/// </remarks>
public interface IValidationAdvisor<in T> : IAdvisor<Operations, T, IList<ErrorFieldViolation>>;
