namespace Schemata.Entity.Owner;

/// <summary>
///     Determines how the owner advisors react when owner resolution returns null.
/// </summary>
public enum OnNullOwnerPolicy
{
    /// <summary>
    ///     Reject the operation. <see cref="Advisors.AdviceAddOwner{TEntity}" /> throws an
    ///     <see cref="Schemata.Abstractions.Exceptions.AuthorizationException" /> before persisting
    ///     an unowned entity; <see cref="Advisors.AdviceBuildQueryOwner{TEntity}" /> forces the query
    ///     to an empty result. This is the safe default.
    /// </summary>
    Reject = 0,

    /// <summary>
    ///     Return an empty result for queries when owner resolution returns null and skip owner
    ///     assignment on add. Useful when unauthenticated callers should receive an empty result.
    /// </summary>
    EmptyResult = 1,

    /// <summary>
    ///     Bypass owner enforcement entirely. Add leaves the owner unset and queries include all
    ///     entities. Only safe when authorization is enforced upstream by other means.
    /// </summary>
    AllowAll = 2,
}
