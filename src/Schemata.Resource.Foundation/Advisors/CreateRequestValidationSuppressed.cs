namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Marker type that, when set in the advice context, suppresses create-request validation.
/// </summary>
/// <remarks>
///     When present in the <see cref="Schemata.Abstractions.Advisors.AdviceContext" />,
///     <see cref="AdviceCreateRequestValidation{TEntity, TRequest}" /> skips validation.
///     Automatically set when <see cref="SchemataResourceOptions.SuppressCreateValidation" /> is <see langword="true" />.
/// </remarks>
internal sealed class CreateRequestValidationSuppressed;
