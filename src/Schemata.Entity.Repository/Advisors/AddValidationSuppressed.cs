namespace Schemata.Entity.Repository.Advisors;

/// <summary>
///     Context flag that suppresses validation during add operations.
/// </summary>
/// <remarks>
///     Set via <see cref="IRepository.SuppressAddValidation" /> or
///     <see cref="IRepository{TEntity}.SuppressAddValidation" />.
///     When present in the <see cref="Schemata.Abstractions.Advisors.AdviceContext" />,
///     <see cref="AdviceAddValidation{TEntity}" /> skips its validation logic.
/// </remarks>
public sealed class AddValidationSuppressed;
