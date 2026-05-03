namespace Schemata.Authorization.Foundation.Authentication;

/// <summary>
///     Security options for the OAuth 2.0 refresh token flow per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc9700.html#section-2.1.3">
///         RFC 9700: The OAuth 2.0 Authorization
///         Framework: Best Current Practice §2.1.3
///     </seealso>
///     .
///     Refresh token rotation is enabled by default.
/// </summary>
public sealed class RefreshTokenFlowOptions
{
    /// <summary>
    ///     When <c>true</c>, the old refresh token is revoked every time a new
    ///     one is issued, preventing refresh token replay per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc9700.html#section-2.1.3">
    ///         RFC 9700: The OAuth 2.0 Authorization
    ///         Framework: Best Current Practice §2.1.3
    ///     </seealso>
    ///     .
    /// </summary>
    public bool RequireRefreshTokenRotation { get; set; } = true;

    /// <summary>Disables refresh token rotation.</summary>
    public RefreshTokenFlowOptions RelaxRefreshTokenRotation() {
        RequireRefreshTokenRotation = false;
        return this;
    }
}
