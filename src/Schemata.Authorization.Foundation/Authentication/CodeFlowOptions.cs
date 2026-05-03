namespace Schemata.Authorization.Foundation.Authentication;

/// <summary>
///     Security options for the OAuth 2.0 authorization code flow.
///     All checks are enabled by default.  Use <c>Relax*</c> methods to
///     disable individual checks (e.g., PKCE enforcement, nonce requirements,
///     single-use code enforcement).
/// </summary>
public sealed class CodeFlowOptions
{
    /// <summary>
    ///     Enforces Proof Key for Code Exchange for all authorization code exchanges,
    ///     per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7636.html">
    ///         RFC 7636: Proof Key for Code Exchange by OAuth Public
    ///         Clients
    ///     </seealso>
    ///     .
    /// </summary>
    public bool RequirePkce { get; set; } = true;

    /// <summary>Requires the PKCE challenge method to be <c>S256</c>.</summary>
    public bool RequirePkceS256 { get; set; } = true;

    /// <summary>
    ///     Prevents PKCE downgrade attacks by rejecting requests where PKCE was not present at the authorization
    ///     endpoint.
    /// </summary>
    public bool RequirePkceDowngradeProtection { get; set; } = true;

    /// <summary>
    ///     Marks authorization codes as redeemed after first use,
    ///     per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc9700.html#section-2.1.2">
    ///         RFC 9700: The OAuth 2.0 Authorization
    ///         Framework: Best Current Practice §2.1.2
    ///     </seealso>
    ///     .
    /// </summary>
    public bool RequireCodeSingleUse { get; set; } = true;

    /// <summary>Requires a nonce when the response_type includes <c>id_token</c>.</summary>
    public bool RequireNonce { get; set; } = true;

    /// <summary>Ensures the response_mode is safe for the given response_type (e.g., no fragmented tokens in form_post).</summary>
    public bool RequireResponseModeSafety { get; set; } = true;

    /// <summary>Disables PKCE enforcement.</summary>
    public CodeFlowOptions RelaxPkce() {
        RequirePkce = false;
        return this;
    }

    /// <summary>Disables the S256 code challenge method requirement.</summary>
    public CodeFlowOptions RelaxPkceS256() {
        RequirePkceS256 = false;
        return this;
    }

    /// <summary>Disables PKCE downgrade protection.</summary>
    public CodeFlowOptions RelaxPkceDowngradeProtection() {
        RequirePkceDowngradeProtection = false;
        return this;
    }

    /// <summary>Disables single-use code enforcement.</summary>
    public CodeFlowOptions RelaxCodeSingleUse() {
        RequireCodeSingleUse = false;
        return this;
    }

    /// <summary>Disables nonce enforcement.</summary>
    public CodeFlowOptions RelaxNonce() {
        RequireNonce = false;
        return this;
    }

    /// <summary>Disables response mode safety enforcement.</summary>
    public CodeFlowOptions RelaxResponseModeSafety() {
        RequireResponseModeSafety = false;
        return this;
    }
}
