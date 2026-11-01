using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Services;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class AuthenticationContextProviderShould
{
    [Fact]
    public async Task Map_Acr_Claim_To_The_Context_Class() {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity([new(Claims.Acr, "urn:example:acs:silver")], "test"));

        var context = await NewProvider().GetContextAsync(principal);

        Assert.Equal("urn:example:acs:silver", context.Acr);
        Assert.Empty(context.Amr);
        Assert.Null(context.AuthTime);
    }

    [Fact]
    public async Task Map_Amr_Claim_As_A_Json_Array() {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity([new(Claims.Amr, """["pwd","otp"]""")], "test"));

        var context = await NewProvider().GetContextAsync(principal);

        Assert.Equal(["pwd", "otp"], context.Amr);
    }

    [Fact]
    public async Task Map_Amr_Claim_As_Whitespace_Separated_Values() {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity([new(Claims.Amr, "pwd otp")], "test"));

        var context = await NewProvider().GetContextAsync(principal);

        Assert.Equal(["pwd", "otp"], context.Amr);
    }

    [Fact]
    public async Task Merge_Multiple_Amr_Claims_Without_Duplicates() {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(Claims.Amr, """["pwd"]"""),
            new(Claims.Amr, "pwd otp"),
        ], "test"));

        var context = await NewProvider().GetContextAsync(principal);

        Assert.Equal(["pwd", "otp"], context.Amr);
    }

    [Fact]
    public async Task Map_Numeric_Auth_Time_Claim_To_Epoch_Seconds() {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity([new(Claims.AuthTime, "1700000000")], "test"));

        var context = await NewProvider().GetContextAsync(principal);

        Assert.Equal(1700000000L, context.AuthTime);
    }

    [Fact]
    public async Task Ignore_A_Non_Numeric_Auth_Time_Claim() {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity([new(Claims.AuthTime, "not-a-number")], "test"));

        var context = await NewProvider().GetContextAsync(principal);

        Assert.Null(context.AuthTime);
    }

    [Fact]
    public async Task Ignore_A_Malformed_Json_Amr_Claim() {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity([new(Claims.Amr, """["pwd" otp]""")], "test"));

        var context = await NewProvider().GetContextAsync(principal);

        Assert.Empty(context.Amr);
    }

    [Fact]
    public async Task Default_To_An_Empty_Context_Without_Context_Claims() {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity([new(IdentityClaims.Subject, "users/u-1")], "test"));

        var context = await NewProvider().GetContextAsync(principal);

        Assert.Null(context.Acr);
        Assert.Empty(context.Amr);
        Assert.Null(context.AuthTime);
    }

    [Fact]
    public async Task Default_To_An_Empty_Context_For_A_Null_Principal() {
        var context = await NewProvider().GetContextAsync(null);

        Assert.Null(context.Acr);
        Assert.Empty(context.Amr);
        Assert.Null(context.AuthTime);
    }

    private static TestAuthenticationContextProvider NewProvider() {
        return new();
    }

    /// <summary>
    ///     Host-side provider: the framework ships no <see cref="IAuthenticationContextProvider" />
    ///     default, so the host owns the mapping from a principal's context claims onto
    ///     <see cref="AuthenticationContext" />.
    /// </summary>
    private sealed class TestAuthenticationContextProvider : IAuthenticationContextProvider
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
}
