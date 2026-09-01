using Schemata.Core.Building;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Marker type in <see cref="Schemata.Abstractions.Advisors.AdviceContext" /> that suppresses
///     update-request validation for the current dispatch. Honored by
///     <see cref="ResourceUpdateValidationPipelineAdvisor{TEntity,TRequest,TDetail}" />, which also
///     reads <see cref="SchemataResourceOptions.SuppressUpdateValidation" /> directly.
/// </summary>
public sealed class UpdateRequestValidationSuppressed;
