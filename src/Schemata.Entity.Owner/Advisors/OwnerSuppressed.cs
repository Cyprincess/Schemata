using Schemata.Entity.Repository;

namespace Schemata.Entity.Owner.Advisors;

/// <summary>
///     Context flag that suppresses automatic owner assignment on add.
/// </summary>
/// <remarks>
///     Set via <see cref="RepositoryExtensions.SuppressOwner" />.
///     When present in the <see cref="Schemata.Abstractions.Advisors.AdviceContext" />,
///     <see cref="AdviceAddOwner{TEntity}" /> will not populate
///     <see cref="Schemata.Abstractions.Entities.IOwnable.Owner" /> on insert.
/// </remarks>
public sealed class OwnerSuppressed;
