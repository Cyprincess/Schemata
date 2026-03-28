namespace Schemata.Authorization.Foundation.Authentication;

/// <summary>Well-known authentication scheme names registered by the Schemata authorization server.</summary>
public static class SchemataAuthorizationSchemes
{
    /// <summary>Default bearer token authentication scheme for validating and issuing OAuth 2.0 access tokens.</summary>
    public const string Bearer = "SchemataAuthorization.Bearer";

    /// <summary>
    ///     Authentication scheme used for authorization-endpoint sign-in flows (issuing authorization codes and hybrid
    ///     tokens).
    /// </summary>
    public const string Code = "SchemataAuthorization.Code";
}
