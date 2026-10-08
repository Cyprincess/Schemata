namespace Schemata.Identity.Foundation;

/// <summary>Configures Schemata identity endpoints.</summary>
public sealed class SchemataIdentityOptions
{

    /// <summary>
    ///     Sign-in page a browser hitting an <c>[Authorize]</c> endpoint without a cookie session is
    ///     sent to. The framework appends a <c>continue</c> parameter holding the original local path,
    ///     protected by ASP.NET Data Protection, which <c>GET ~/Authenticate/Continue</c> unprotects
    ///     and redirects back to. Leave unset to answer such requests with 401 instead.
    /// </summary>
    public string? LoginUri { get; set; }
}
