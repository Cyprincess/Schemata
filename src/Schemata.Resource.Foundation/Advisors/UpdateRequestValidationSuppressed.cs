namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Marker type in <see cref="Schemata.Abstractions.Advisors.AdviceContext" /> that suppresses
///     update-request validation for the current dispatch. Honored by
///     <see cref="ResourceUpdateValidationPipelineAdvisor{TEntity,TRequest,TDetail}" />.
/// </summary>
public sealed class UpdateRequestValidationSuppressed;
