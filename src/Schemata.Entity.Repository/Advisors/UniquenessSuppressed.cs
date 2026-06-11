namespace Schemata.Entity.Repository.Advisors;

/// <summary>
///     Context marker disabling <see cref="AdviceAddUniqueness{TEntity}" /> for flows
///     that tolerate duplicate keys or cannot afford the pre-insert lookup.
/// </summary>
public sealed class UniquenessSuppressed;
