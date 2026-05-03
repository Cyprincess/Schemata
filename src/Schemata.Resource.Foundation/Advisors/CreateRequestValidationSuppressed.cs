namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Marker type in <see cref="Schemata.Abstractions.Advisors.AdviceContext" /> that suppresses
///     create-request validation.
///     Set automatically when <see cref="SchemataResourceOptions.SuppressCreateValidation" /> is
///     <see langword="true" />.
/// </summary>
public sealed class CreateRequestValidationSuppressed;
