using System.Collections.Generic;
using System.Security.Claims;

namespace Schemata.Authorization.Skeleton;

/// <summary>
///     Result from an authorization endpoint handler.
///     The <see cref="Status" /> property determines how the caller should proceed.
/// </summary>
/// <remarks>
///     <see cref="SignIn" /> is the success path — the pipeline continues.
///     <see cref="Redirect" /> and <see cref="Content" /> are final for the HTTP layer.
///     <see cref="Challenge" /> means the client could not be authenticated.
/// </remarks>
public sealed class AuthorizationResult
{
    private AuthorizationResult(
        AuthorizationStatus          status,
        ClaimsPrincipal?             principal,
        Dictionary<string, string?>? properties
    ) {
        Status     = status;
        Principal  = principal;
        Properties = properties;
    }

    /// <summary>Outcome that drives downstream handling.</summary>
    public AuthorizationStatus Status { get; }

    /// <summary>Non-null when <see cref="Status" /> is <see cref="AuthorizationStatus.SignIn" />.</summary>
    public ClaimsPrincipal? Principal { get; }

    /// <summary>
    ///     Forwarded to the caller alongside <see cref="SignIn" /> so the outer HTTP handler can
    ///     attach them to the callback redirect query string.
    /// </summary>
    public Dictionary<string, string?>? Properties { get; }

    /// <summary>Non-null when <see cref="Status" /> is <see cref="AuthorizationStatus.Redirect" />.</summary>
    public string? RedirectUri { get; set; }

    /// <summary>Non-null when <see cref="Status" /> is <see cref="AuthorizationStatus.Content" />.</summary>
    public object? Data { get; set; }

    /// <summary>Creates a result indicating the principal is authenticated.</summary>
    public static AuthorizationResult SignIn(ClaimsPrincipal principal, Dictionary<string, string?>? properties = null) {
        return new(AuthorizationStatus.SignIn, principal, properties);
    }

    /// <summary>Creates a result that redirects the user agent to <paramref name="uri" />.</summary>
    public static AuthorizationResult Redirect(string uri) {
        return new(AuthorizationStatus.Redirect, null, null) { RedirectUri = uri };
    }

    /// <summary>Creates a result that returns <paramref name="data" /> as the HTTP response body.</summary>
    public static AuthorizationResult Content(object? data) {
        return new(AuthorizationStatus.Content, null, null) { Data = data };
    }

    /// <summary>
    ///     Creates a result that instructs the HTTP layer to challenge the client.
    ///     When <paramref name="scheme" /> is non-null it specifies the authentication scheme.
    /// </summary>
    public static AuthorizationResult Challenge(string? scheme = null) {
        return new(AuthorizationStatus.Challenge, null, null) { Data = scheme };
    }
}
