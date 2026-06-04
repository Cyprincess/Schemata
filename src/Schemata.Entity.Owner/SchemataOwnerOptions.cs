namespace Schemata.Entity.Owner;

/// <summary>
///     Options governing the behavior of the owner advisors when
///     <see cref="IOwnerResolver{TEntity}" /> cannot resolve an owner.
/// </summary>
public sealed class SchemataOwnerOptions
{
    /// <summary>
    ///     Gets or sets the policy applied when the resolver returns <see langword="null" />
    ///     or an empty string. Defaults to <see cref="OnNullOwnerPolicy.Reject" /> so unowned
    ///     entities and unauthenticated queries fail closed.
    /// </summary>
    public OnNullOwnerPolicy OnNullOwner { get; set; } = OnNullOwnerPolicy.Reject;
}

/// <summary>
///     Determines how the owner advisors react when no owner can be resolved.
/// </summary>
public enum OnNullOwnerPolicy
{
    /// <summary>
    ///     Reject the operation. <see cref="Advisors.AdviceAddOwner{TEntity}" /> throws an
    ///     <see cref="Schemata.Abstractions.Exceptions.AuthorizationException" /> rather
    ///     than persisting an unowned entity; <see cref="Advisors.AdviceBuildQueryOwner{TEntity}" />
    ///     forces the query to an empty result. This is the safe default.
    /// </summary>
    Reject = 0,

    /// <summary>
    ///     Return an empty result for queries when no owner is resolved and skip owner
    ///     assignment on add. Useful when unauthenticated callers should see nothing rather
    ///     than receive an error.
    /// </summary>
    EmptyResult = 1,

    /// <summary>
    ///     Bypass owner enforcement entirely. Add will leave the owner unset and queries will
    ///     not be filtered. Only safe when authorization is enforced upstream by other means.
    /// </summary>
    AllowAll = 2,
}
