namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Marker type in <see cref="Schemata.Abstractions.Advisors.AdviceContext" /> that suppresses
///     update-request validation.
///     Set automatically when <see cref="SchemataResourceOptions.SuppressUpdateValidation" /> is
///     <see langword="true" />.
/// </summary>
public sealed class UpdateRequestValidationSuppressed;
