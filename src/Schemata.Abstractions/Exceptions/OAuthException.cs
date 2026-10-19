using System;
using System.Collections.Generic;
using System.Text;
using Schemata.Abstractions.Errors;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

/// <summary>
///     Thrown for OAuth 2.0 protocol errors.
/// </summary>
/// <remarks>
///     Produces an <see cref="OAuthErrorResponse" /> per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html">RFC 6749: The OAuth 2.0 Authorization Framework</seealso>
///     for protocol-specific error serialization. The developer-facing description and the
///     locale-aware detail render from the same structured facts — the resx template key and
///     its named arguments — so the invariant message and every localized variant stay
///     consistent.
/// </remarks>
public class OAuthException : SchemataException
{
    /// <summary>
    ///     Initializes a new <see cref="OAuthException" /> from a resx template key.
    /// </summary>
    /// <param name="error">OAuth 2.0 error code (e.g. <c>"invalid_grant"</c>).</param>
    /// <param name="resourceKey">
    ///     The <see cref="SchemataResources" /> data name identifying the error template;
    ///     it also becomes the <see cref="ErrorInfoDetail.Reason" />.
    /// </param>
    /// <param name="args">Optional named arguments substituted into the template.</param>
    /// <param name="code">HTTP response status code.</param>
    public OAuthException(
        string                                     error,
        string                                     resourceKey,
        IReadOnlyDictionary<string, string?>? args = null,
        int                                        code = 400
    ) : base(code, error, LocalizedMessageFormatter.FormatInvariant(resourceKey, args)) {
        Details = [new ErrorInfoDetail { Reason = resourceKey, Domain = ErrorDomains.OAuth }];
        AttachMetadata(args);
    }

    private OAuthException(string error, string description, int code) : base(code, error, description) { }

    /// <summary>
    ///     Builds an exception carrying a caller-supplied description. Reserved for extension
    ///     points whose validation text is owned by application code (for example
    ///     authorization-detail type descriptors); framework errors use the resx-keyed
    ///     constructor so the invariant description and every localized variant render from
    ///     the same template facts.
    /// </summary>
    /// <param name="error">OAuth 2.0 error code (e.g. <c>"invalid_client"</c>).</param>
    /// <param name="description">Caller-owned human-readable description.</param>
    /// <param name="code">HTTP response status code.</param>
    public static OAuthException FromDescription(string error, string description, int code = 400) {
        return new(error, description, code);
    }

    /// <inheritdoc />
    public override string? Domain => ErrorDomains.OAuth;



    /// <summary>
    ///     Redirect target used to deliver the error through the interactive authorization flow.
    /// </summary>
    /// <remarks>
    ///     Used by the interactive authorization endpoint after <c>redirect_uri</c>
    ///     validation succeeds, per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-4.1.2.1">
    ///         RFC 6749: The OAuth 2.0 Authorization
    ///         Framework §4.1.2.1: Error Response
    ///     </seealso>
    ///     .
    /// </remarks>
    public string? RedirectUri { get; set; }

    /// <summary>
    ///     Opaque <c>state</c> parameter echoed back to the client during redirect-based
    ///     error delivery, per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-4.1.2.1">
    ///         RFC 6749: The OAuth 2.0 Authorization
    ///         Framework §4.1.2.1: Error Response
    ///     </seealso>
    ///     .
    /// </summary>
    public string? State { get; set; }

    /// <summary>
    ///     OAuth 2.0 <c>response_mode</c> value controlling how the error is transported
    ///     to the client (e.g. <c>"query"</c>, <c>"fragment"</c>, <c>"form_post"</c>).
    /// </summary>
    public string? ResponseMode { get; set; }

    /// <summary>
    ///     URI identifying a human-readable web page with information about the error,
    ///     per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-5.2">
    ///         RFC 6749: The OAuth 2.0 Authorization
    ///         Framework §5.2: Error Response
    ///     </seealso>
    ///     .
    /// </summary>
    public string? ErrorUri { get; set; }

    /// <summary>
    ///     Additional HTTP response headers rendered with the error response (e.g.
    ///     <c>DPoP-Nonce</c> alongside a <c>use_dpop_nonce</c> challenge), per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc9449.html#section-8">
    ///         RFC 9449: OAuth 2.0 Demonstrating Proof-of-Possession at the Application Layer
    ///         (DPoP) §8: Authorization Server-Provided Nonce
    ///     </seealso>
    ///     .
    /// </summary>
    public IDictionary<string, string>? Headers { get; set; }

    /// <summary>
    ///     Marks the error as parameter-free on the wire: the endpoint answers with the bare
    ///     HTTP status code and carries no Error Response parameters, per
    ///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#AuthError">
    ///         OpenID Connect Core 1.0 §3.1.2.6: Authentication Error Response
    ///     </seealso>
    ///     .
    /// </summary>
    /// <remarks>
    ///     Reserved for failures whose legal delivery encoding cannot be determined, such as an
    ///     unsupported <c>response_mode</c>. Ordinary OAuth errors keep the RFC 6749 §5.2 JSON
    ///     envelope produced by <see cref="CreateErrorResponse" />.
    /// </remarks>
    public bool OmitErrorParameters { get; set; }

    public override object? CreateErrorResponse(string? requestId = null, string? locale = null) {
        var status  = Status ?? ErrorCodes.Internal;
        var details = new List<IErrorDetail>();

        if (Details is { Count: > 0 }) {
            details.AddRange(Details);
        }

        // The OAuth `error` wire value is RFC 6749 lowercase (e.g. "invalid_grant"); the AIP-193
        // structured Reason / resx data name uses UPPER_SNAKE_CASE. Normalize for ErrorInfoDetail
        // and EnsureLocalizedMessage so OAuth errors localize through the same resx pipeline as
        // every other SchemataException.
        var reason = status.ToUpperInvariant();
        EnsureErrorInfo(details, reason, Domain);
        EnsureRequestInfo(details, requestId);
        EnsureLocalizedMessage(details, locale, reason);

        return new OAuthErrorResponse {
            Error            = status,
            ErrorDescription = SanitizeDescription(Message),
            ErrorUri         = ErrorUri,
            Details          = details,
        };
    }
    // RFC 6749 §5.2 allows printable ASCII except DQUOTE and backslash. Encode complete
    // Unicode scalars so redirect and JSON transports share one valid wire description.
    private static string? SanitizeDescription(string? description) {
        if (string.IsNullOrEmpty(description)) {
            return description;
        }

        var builder = new StringBuilder(description.Length);
        Span<byte> utf8 = stackalloc byte[4];
        foreach (var rune in description.EnumerateRunes()) {
            if (rune.Value is >= 0x20 and <= 0x21 or >= 0x23 and <= 0x5B or >= 0x5D and <= 0x7E) {
                builder.Append(rune.ToString());
                continue;
            }

            var written = rune.EncodeToUtf8(utf8);
            for (var i = 0; i < written; i++) {
                builder.Append('%').Append(utf8[i].ToString("X2"));
            }
        }

        return builder.ToString();
    }

}
