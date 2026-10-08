using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Claims;
using Schemata.Identity.Skeleton;

namespace Schemata.Identity.Foundation;

internal static class AuthenticationClaims
{
    internal static void Stamp(ClaimsPrincipal principal, string? requested, bool multifactor, TimeProvider time) {
        var identity = (ClaimsIdentity)principal.Identity!;
        while (identity.FindFirst("amr") is { } method) identity.RemoveClaim(method);
        while (identity.FindFirst("acr") is { } context) identity.RemoveClaim(context);
        while (identity.FindFirst("auth_time") is { } timestamp) identity.RemoveClaim(timestamp);
        var achieved = multifactor ? AuthenticationContextClasses.Multifactor : AuthenticationContextClasses.Password;
        identity.AddClaim(new("auth_time", time.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64));
        identity.AddClaim(new("acr", Resolve(requested, achieved)));
        identity.AddClaim(new("amr", multifactor ? "[\"pwd\",\"otp\",\"mfa\"]" : "[\"pwd\"]", "JSON"));
    }

    private static string Resolve(string? requested, string achieved) {
        if (string.IsNullOrWhiteSpace(requested)) return achieved;
        var classes = AuthenticationContextClasses.Supported;
        var reached = Rank(classes, achieved);
        if (reached < 0) return achieved;
        foreach (var value in requested.Split(' ', StringSplitOptions.RemoveEmptyEntries)) {
            if (Rank(classes, value) >= reached) return value;
        }
        return achieved;
    }

    private static int Rank(IReadOnlyList<string> classes, string value) {
        for (var i = 0; i < classes.Count; i++) if (classes[i] == value) return i;
        return -1;
    }

    internal static void Carry(ClaimsPrincipal source, ClaimsPrincipal target) {
        var identity = (ClaimsIdentity)target.Identity!;
        foreach (var claim in source.Claims) {
            if (claim.Type is "amr" or "acr" or "auth_time") {
                identity.AddClaim(new(claim.Type, claim.Value, claim.ValueType));
            }
        }
    }
}
