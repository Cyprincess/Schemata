namespace Schemata.Entity.Repository.Advisors;

/// <summary>
///     Context marker disabling <see cref="AdviceAddUniqueness{TEntity}" /> for flows
///     that tolerate duplicate keys or cannot afford the pre-insert lookup.
/// </summary>
/// <remarks>
///     Opt in with <c>AdviceContext.Set&lt;T&gt;(...)</c> or scope suppression with
///     <c>AdviceContext.Use&lt;T&gt;()</c>. No builder toggle controls this marker.
/// </remarks>
public sealed class UniquenessSuppressed;
