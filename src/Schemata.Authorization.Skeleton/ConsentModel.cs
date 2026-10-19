namespace Schemata.Authorization.Skeleton;

/// <summary>
///     The consent model an authorization request is evaluated under.
/// </summary>
public enum ConsentModel
{
    /// <summary>The user must explicitly grant consent; a stored prior authorization covers repeat requests.</summary>
    Explicit,

    /// <summary>Consent is granted without user interaction unless the request asks for the consent prompt.</summary>
    Implicit,

    /// <summary>Consent is managed outside the authorization server; only a stored prior authorization satisfies it.</summary>
    External,
}
