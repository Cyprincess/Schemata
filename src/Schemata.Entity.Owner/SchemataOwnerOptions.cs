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