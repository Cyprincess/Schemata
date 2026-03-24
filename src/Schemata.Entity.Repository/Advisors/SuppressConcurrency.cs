namespace Schemata.Entity.Repository.Advisors;

/// <summary>
///     Context flag that suppresses concurrency-stamp checks and generation.
/// </summary>
/// <remarks>
///     Set via <see cref="IRepository.SuppressConcurrency" /> or <see cref="IRepository{TEntity}.SuppressConcurrency" />.
///     When present in the <see cref="Schemata.Abstractions.Advisors.AdviceContext" />,
///     <see cref="AdviceAddConcurrency{TEntity}" /> and <see cref="AdviceUpdateConcurrency{TEntity}" /> skip their logic.
/// </remarks>
internal sealed class SuppressConcurrency;
