namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Marker type in <see cref="Schemata.Abstractions.Advisors.AdviceContext" /> that suppresses
///     create-request validation for the current dispatch. Honored by
///     <see cref="ResourceCreateValidationPipelineAdvisor{TEntity,TRequest,TDetail}" />, which also
///     reads <see cref="SchemataResourceOptions.SuppressCreateValidation" /> directly.
/// </summary>
public sealed class CreateRequestValidationSuppressed;
