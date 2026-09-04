namespace Schemata.Authorization.Foundation.Authentication;

/// <summary>Well-known authorization policy names registered by the Schemata authorization server.</summary>
public static class SchemataAuthorizationPolicies
{
    /// <summary>
    ///     Access-token policy for the UserInfo-style endpoints: authenticates the Bearer scheme,
    ///     plus the DPoP scheme when the DPoP flow feature is installed.
    /// </summary>
    public const string Profile = "Schemata.Authorization.Profile";
}
