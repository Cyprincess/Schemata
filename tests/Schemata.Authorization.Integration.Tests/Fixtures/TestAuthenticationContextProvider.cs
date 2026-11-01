using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Services;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Integration.Tests.Fixtures;

/// <summary>
///     Host-side provider: the framework ships no <see cref="IAuthenticationContextProvider" />
///     default, so this test host maps a principal's context claims onto
///     <see cref="AuthenticationContext" /> itself.
/// </summary>
public sealed class TestAuthenticationContextProvider : IAuthenticationContextProvider
{
    public Task<AuthenticationContext> GetContextAsync(ClaimsPrincipal? principal, CancellationToken ct = default) {
        if (principal is null) {
            return Task.FromResult(new AuthenticationContext(null, [], null));
        }

        var acr = principal.FindFirstValue(Claims.Acr);
        if (string.IsNullOrWhiteSpace(acr)) {
            acr = null;
        }

        var amr = new List<string>();
        foreach (var claim in principal.FindAll(Claims.Amr)) {
            foreach (var value in ParseAmrValues(claim.Value)) {
                if (!amr.Contains(value)) {
                    amr.Add(value);
                }
            }
        }

        long? authTime = null;
        var raw = principal.FindFirstValue(Claims.AuthTime);
        if (!string.IsNullOrWhiteSpace(raw)
         && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var epoch)) {
            authTime = epoch;
        }

        return Task.FromResult(new AuthenticationContext(acr, amr, authTime));
    }

    private static IEnumerable<string> ParseAmrValues(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return [];
        }

        var trimmed = value.Trim();
        if (!trimmed.StartsWith('[')) {
            return trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }

        try {
            return JsonSerializer.Deserialize<string[]>(trimmed) ?? [];
        } catch (JsonException) {
            return [];
        }
    }
}
