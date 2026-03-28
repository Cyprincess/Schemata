namespace Schemata.Entity.Repository.Advisors;

/// <summary>
///     Context flag that suppresses automatic timestamp assignment on add and update operations.
/// </summary>
/// <remarks>
///     Set via <see cref="IRepository.SuppressTimestamp" /> or <see cref="IRepository{TEntity}.SuppressTimestamp" />.
///     When present in the <see cref="Schemata.Abstractions.Advisors.AdviceContext" />,
///     <see cref="AdviceAddTimestamp{TEntity}" /> and <see cref="AdviceUpdateTimestamp{TEntity}" /> skip their logic.
/// </remarks>
internal sealed class TimestampSuppressed;
