namespace Schemata.Entity.Repository.Advisors;

/// <summary>
///     Context flag that suppresses validation during update operations.
/// </summary>
/// <remarks>
///     Set via <see cref="IRepository.SuppressUpdateValidation" /> or
///     <see cref="IRepository{TEntity}.SuppressUpdateValidation" />.
///     When present in the <see cref="Schemata.Abstractions.Advisors.AdviceContext" />,
///     <see cref="AdviceUpdateValidation{TEntity}" /> skips its validation logic.
/// </remarks>
public sealed class UpdateValidationSuppressed;
