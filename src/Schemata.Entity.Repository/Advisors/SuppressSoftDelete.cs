namespace Schemata.Entity.Repository.Advisors;

/// <summary>
///     Context flag that suppresses the soft-delete behavior on add and remove operations.
/// </summary>
/// <remarks>
///     Set via <see cref="IRepository.SuppressSoftDelete" /> or <see cref="IRepository{TEntity}.SuppressSoftDelete" />.
///     When present in the <see cref="Schemata.Abstractions.Advisors.AdviceContext" />,
///     <see cref="AdviceAddSoftDelete{TEntity}" /> will not clear <see cref="Schemata.Abstractions.Entities.ISoftDelete.DeleteTime" /> on insert and
///     <see cref="AdviceRemoveSoftDelete{TEntity}" /> will not convert deletes into soft-deletes.
/// </remarks>
internal sealed class SuppressSoftDelete;
