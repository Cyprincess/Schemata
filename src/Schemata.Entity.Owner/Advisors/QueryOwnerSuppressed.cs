using Schemata.Entity.Repository;

namespace Schemata.Entity.Owner.Advisors;

/// <summary>
///     Context flag that suppresses the owner-scoped query filter.
/// </summary>
/// <remarks>
///     Set via <see cref="RepositoryExtensions.SuppressQueryOwner" />.
///     When present in the <see cref="Schemata.Abstractions.Advisors.AdviceContext" />,
///     <see cref="AdviceBuildQueryOwner{TEntity}" /> will not restrict queries to the current caller's
///     <see cref="Schemata.Abstractions.Entities.IOwnable.Owner" />.
/// </remarks>
public sealed class QueryOwnerSuppressed;
