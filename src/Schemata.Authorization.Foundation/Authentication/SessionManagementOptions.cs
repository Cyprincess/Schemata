namespace Schemata.Authorization.Foundation.Authentication;

/// <summary>Configuration for OpenID Connect Session Management.</summary>
public sealed class SessionManagementOptions
{
    /// <summary>Name of the non-HttpOnly cookie carrying opaque OP User-Agent state.</summary>
    public string OpStateCookieName { get; set; } = "schemata.opstate";
}
