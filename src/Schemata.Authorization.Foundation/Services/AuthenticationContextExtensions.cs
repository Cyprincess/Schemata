using System.Collections.Generic;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Schemata.Authorization.Skeleton.Services;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     Transports an <see cref="AuthenticationContext" /> across flows whose sign-in principal
///     carries no evidence of its own, by writing it as the bare OIDC claims a host
///     <c>IAuthenticationContextProvider</c> reads back at claim assembly.
/// </summary>
internal static class AuthenticationContextExtensions
{
    /// <summary>
    ///     Adds the context as bare <c>acr</c>, <c>amr</c> (JSON array), and <c>auth_time</c>
    ///     claims; only members with evidence are added. Destination tagging happens later in
    ///     the claims pipeline.
    /// </summary>
    internal static void Stamp(this List<Claim> claims, AuthenticationContext context) {
        if (!string.IsNullOrWhiteSpace(context.Acr)) {
            claims.Add(new(Claims.Acr, context.Acr));
        }

        if (context.Amr is { Count: > 0 }) {
            claims.Add(new(Claims.Amr, JsonSerializer.Serialize(context.Amr), JsonClaimValueTypes.Json));
        }

        if (context.AuthTime is not null) {
            claims.Add(new(Claims.AuthTime, context.AuthTime.Value.ToString(CultureInfo.InvariantCulture)));
        }
    }
}
