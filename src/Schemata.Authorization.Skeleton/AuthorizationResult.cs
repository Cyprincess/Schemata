using System.Collections.Generic;
using System.Security.Claims;

namespace Schemata.Authorization.Skeleton;

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

    /// <summary>Outcome of the authorization operation, drives downstream handling.</summary>
    public AuthorizationStatus Status { get; }

    /// <summary>Non-null when Status is SignIn.</summary>
    public ClaimsPrincipal? Principal { get; }

    /// <summary>Key-value pairs forwarded to the authentication handler (e.g. callback query parameters).</summary>
    public Dictionary<string, string?>? Properties { get; }

    /// <summary>Non-null when Status is Redirect or Callback.</summary>
    public string? RedirectUri { get; set; }

    /// <summary>Non-null when Status is Content.</summary>
    public object? Data { get; set; }

    public static AuthorizationResult SignIn(ClaimsPrincipal principal, Dictionary<string, string?>? properties = null) {
        return new(AuthorizationStatus.SignIn, principal, properties);
    }

    public static AuthorizationResult Redirect(string uri) {
        return new(AuthorizationStatus.Redirect, null, null) { RedirectUri = uri };
    }

    public static AuthorizationResult Content(object? data) {
        return new(AuthorizationStatus.Content, null, null) { Data = data };
    }

    public static AuthorizationResult Challenge(string? scheme = null) {
        return new(AuthorizationStatus.Challenge, null, null) { Data = scheme };
    }
}
