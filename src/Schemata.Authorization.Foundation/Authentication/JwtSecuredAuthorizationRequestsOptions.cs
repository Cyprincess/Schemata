using System;
using System.Collections.Generic;

namespace Schemata.Authorization.Foundation.Authentication;

/// <summary>Configuration for JWT-Secured Authorization Requests.</summary>
public sealed class JwtSecuredAuthorizationRequestsOptions
{
    /// <summary>JWS algorithms accepted on signed authorization request objects.</summary>
    public HashSet<string> SigningAlgorithms { get; } = new(StringComparer.Ordinal);

    /// <summary>Requires every authorization request to carry a signed request object.</summary>
    public bool RequireForAllClients { get; set; }
}
