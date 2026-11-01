using System;

namespace Schemata.Authorization.Foundation.Authentication;

/// <summary>Configuration for OAuth 2.0 Pushed Authorization Requests.</summary>
public sealed class PushedAuthorizationRequestsOptions
{
    /// <summary>Lifetime of a pushed authorization request before its request URI expires.</summary>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Requires every authorization request to use a request URI issued by the PAR endpoint.</summary>
    public bool RequireForAllClients { get; set; }

    /// <summary>Allows an unexpired request URI to be reused after its first authorization request.</summary>
    public bool AllowRequestUriReplay { get; set; }
}
