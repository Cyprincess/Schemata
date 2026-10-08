using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Skeleton.Models;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class ClaimsValueQualifierShould
{
    [Fact]
    public async Task Emit_A_Value_Qualified_Claim_In_The_Id_Token_When_The_Final_Value_Matches() {
        var destinations = await IdTokenDestinations(new() { Value = "a@example.com" }, "a@example.com");

        Assert.Contains(ClaimDestinations.IdentityToken, destinations);
    }

    [Fact]
    public async Task Omit_The_Claim_From_The_Id_Token_When_The_Value_Qualifier_Mismatches() {
        var destinations = await IdTokenDestinations(new() { Value = "a@example.com" }, "b@example.com");

        Assert.DoesNotContain(ClaimDestinations.IdentityToken, destinations);
    }

    [Fact]
    public async Task Emit_A_Values_Qualified_Claim_In_The_Id_Token_When_One_Value_Matches() {
        var destinations = await IdTokenDestinations(new() { Values = ["x", "y"] }, "y");

        Assert.Contains(ClaimDestinations.IdentityToken, destinations);
    }

    [Fact]
    public async Task Omit_The_Claim_From_The_Id_Token_When_No_Values_Qualifier_Matches() {
        var destinations = await IdTokenDestinations(new() { Values = ["x", "y"] }, "z");

        Assert.DoesNotContain(ClaimDestinations.IdentityToken, destinations);
    }

    [Fact]
    public async Task Emit_A_Claim_Requested_In_The_Default_Manner_In_The_Id_Token() {
        var destinations = await IdTokenDestinations(null, "b@example.com");

        Assert.Contains(ClaimDestinations.IdentityToken, destinations);
    }

    [Fact]
    public async Task Emit_A_Value_Qualified_Claim_In_Userinfo_When_The_Final_Value_Matches() {
        var destinations = await UserinfoDestinations(new() { Value = "a@example.com" }, "a@example.com");

        Assert.Contains(ClaimDestinations.UserInfo, destinations);
    }

    [Fact]
    public async Task Omit_The_Claim_From_Userinfo_When_The_Value_Qualifier_Mismatches() {
        var destinations = await UserinfoDestinations(new() { Value = "a@example.com" }, "b@example.com");

        Assert.DoesNotContain(ClaimDestinations.UserInfo, destinations);
    }

    [Fact]
    public async Task Emit_A_Values_Qualified_Claim_In_Userinfo_When_One_Value_Matches() {
        var destinations = await UserinfoDestinations(new() { Values = ["x", "y"] }, "y");

        Assert.Contains(ClaimDestinations.UserInfo, destinations);
    }

    [Fact]
    public async Task Omit_The_Claim_From_Userinfo_When_No_Values_Qualifier_Matches() {
        var destinations = await UserinfoDestinations(new() { Values = ["x", "y"] }, "z");

        Assert.DoesNotContain(ClaimDestinations.UserInfo, destinations);
    }

    [Fact]
    public async Task Emit_A_Claim_Requested_In_The_Default_Manner_In_Userinfo() {
        var destinations = await UserinfoDestinations(null, "b@example.com");

        Assert.Contains(ClaimDestinations.UserInfo, destinations);
    }

    [Fact]
    public async Task Surface_A_Malformed_Persisted_Userinfo_Request_As_A_Configuration_Error() {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(Claims.UserinfoRequest, "not json")], "test"));
        var destinations = new HashSet<string>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => new AdviceDestinationUserinfoRequest().AdviseAsync(
            Context(), new("email", "a@example.com"), destinations, principal, new AuthorizationClaimContext()));
    }

    [Fact]
    public void Preserve_The_Qualifiers_Through_The_Token_Persistence_Round_Trip() {
        var userinfo = new Dictionary<string, ClaimsRequestSpec?> {
            ["sub"]   = null,
            ["email"] = new() { Essential = true, Value = "a@example.com" },
            ["zone"]  = new() { Values = ["eu", "us"] },
        };

        var restored = ClaimsRequest.FromUserinfoRequest(ClaimsRequest.SerializeUserinfo(userinfo));

        Assert.NotNull(restored?.Userinfo);
        Assert.Null(restored.Userinfo["sub"]);
        Assert.Equal("a@example.com", restored.Userinfo["email"]?.Value);
        Assert.True(restored.Userinfo["email"]?.Essential);
        Assert.Equal(["eu", "us"], restored.Userinfo["zone"]?.Values);
    }

    private static async Task<HashSet<string>> IdTokenDestinations(ClaimsRequestSpec? spec, string actual) {
        var issuance = new AuthorizationClaimContext {
            RequestedClaims = new() { IdToken = new() { ["email"] = spec } },
        };
        var destinations = new HashSet<string>();
        await new AdviceDestinationClaimsRequest().AdviseAsync(
            Context(), new("email", actual), destinations, new ClaimsPrincipal(), issuance);
        return destinations;
    }

    private static async Task<HashSet<string>> UserinfoDestinations(ClaimsRequestSpec? spec, string actual) {
        var persisted = ClaimsRequest.SerializeUserinfo(new Dictionary<string, ClaimsRequestSpec?> { ["email"] = spec });
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(Claims.UserinfoRequest, persisted)], "test"));
        var destinations = new HashSet<string>();
        await new AdviceDestinationUserinfoRequest().AdviseAsync(
            Context(), new("email", actual), destinations, principal, new AuthorizationClaimContext());
        return destinations;
    }

    private static AdviceContext Context() {
        return new(new ServiceCollection().BuildServiceProvider());
    }
}
