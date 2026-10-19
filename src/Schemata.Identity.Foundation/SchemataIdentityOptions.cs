using Microsoft.AspNetCore.Identity;

namespace Schemata.Identity.Foundation;

/// <summary>Configures Schemata identity endpoints.</summary>
public sealed class SchemataIdentityOptions
{
    /// <summary>
    ///     Scheme receiving the application cookie sign-in. The global default is the native
    ///     Identity application scheme; a tenant profile composes its own <c>UseCookie</c>
    ///     scheme name here so its sign-in and sign-out address the tenant closure.
    /// </summary>
    public string ApplicationScheme { get; set; } = IdentityConstants.ApplicationScheme;

    /// <summary>
    ///     Scheme receiving the bearer ticket issued by the sign-in and refresh endpoints. The
    ///     global default is the native Identity bearer scheme; a tenant profile that enables
    ///     the tenant bearer closure names its own scheme here.
    /// </summary>
    public string BearerScheme { get; set; } = IdentityConstants.BearerScheme;

    /// <summary>
    ///     Sign-in page a browser hitting an <c>[Authorize]</c> endpoint without a cookie session is
    ///     sent to. The framework appends a <c>continue</c> parameter holding the original local path,
    ///     protected by ASP.NET Data Protection, which <c>GET ~/Authenticate/Continue</c> unprotects
    ///     and redirects back to. Leave unset to answer such requests with 401 instead.
    /// </summary>
    public string? LoginUri { get; set; }
}
