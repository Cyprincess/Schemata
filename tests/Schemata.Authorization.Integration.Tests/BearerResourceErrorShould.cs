using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Integration.Tests;

/// <summary>
///     RFC 6750 §3.1 over real HTTP: any presented Bearer access token that fails validation —
///     unknown, failed signature, or wrong <c>typ</c> — is rejected with the same 401
///     <c>invalid_token</c> challenge, so a stored-but-invalid token is indistinguishable from an
///     unknown one.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Layer", "Component")]
public class BearerResourceErrorShould
{
    [Fact]
    public async Task Reject_An_Unknown_Token_With_Invalid_Token() {
        using var factory = new WebAppFactory();

        var challenge = await ChallengeFor(factory, "unknown-" + Guid.NewGuid().ToString("n"));

        Assert.StartsWith($"error=\"{OAuthErrors.InvalidToken}\"", challenge);
    }

    [Fact]
    public async Task Reject_A_Stored_Token_Whose_Payload_Fails_Signature_Like_An_Unknown_Token() {
        using var factory = new WebAppFactory();
        var       unknown = await ChallengeFor(factory, "unknown-" + Guid.NewGuid().ToString("n"));

        var tampered  = await Mint(factory, typ: TokenMediaTypes.AccessToken, tamperSignature: true);
        var challenge = await ChallengeFor(factory, tampered);

        Assert.Equal(unknown, challenge);
    }

    [Fact]
    public async Task Reject_A_Stored_Token_With_A_Wrong_Typ_Like_An_Unknown_Token() {
        using var factory = new WebAppFactory();
        var       unknown = await ChallengeFor(factory, "unknown-" + Guid.NewGuid().ToString("n"));

        var wrongTyp  = await Mint(factory, typ: "jwt");
        var challenge = await ChallengeFor(factory, wrongTyp);

        Assert.Equal(unknown, challenge);
    }

    private static async Task<string> ChallengeFor(WebAppFactory factory, string token) {
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoints.Profile);
        request.Headers.Authorization = new(Schemes.Bearer, token);
        using var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenge = Assert.Single(response.Headers.WwwAuthenticate);
        Assert.Equal(Schemes.Bearer, challenge.Scheme, ignoreCase: true);
        Assert.NotNull(challenge.Parameter);
        return challenge.Parameter!;
    }

    private static async Task<string> Mint(WebAppFactory factory, string? typ = null, bool tamperSignature = false) {
        using var scope    = factory.Services.CreateScope();
        var       services = scope.ServiceProvider;
        var application = await services.GetRequiredService<IApplicationManager<SchemataApplication>>()
                                        .FindByClientIdAsync("test-client");
        Assert.NotNull(application);

        var claims = new List<Claim> {
            new(IdentityClaims.Subject, "users/u-1"),
            new(Claims.Audience, "https://localhost"),
            new(Claims.Scope, Scopes.OpenId + " " + Scopes.Profile),
            new(Claims.ClientId, "test-client"),
        };
        var issuedAt = DateTimeOffset.UtcNow;

        var issuer = services.GetRequiredService<TokenService>();
        await using var signing = await issuer.BeginSigningAsync();
        var value = issuer.CreateToken(
            signing, signing.Primary, claims, issuedAt, issuedAt + TimeSpan.FromHours(1), typ: typ);

        if (tamperSignature) {
            var segments  = value.Split('.');
            var signature = segments[^1].ToCharArray();
            signature[0] = signature[0] == 'A' ? 'B' : 'A';
            segments[^1] = new(signature);
            value        = string.Join('.', segments);
        }

        await services.GetRequiredService<ITokenStore<SchemataToken>>().CreateAsync(new() {
            Type        = TokenTypes.AccessToken,
            Format      = TokenFormats.Jwt,
            Status      = TokenStatuses.Valid,
            ReferenceId = value,
            Payload     = value,
            Parent      = "users/u-1",
            Application = application.CanonicalName,
            ExpireTime  = (issuedAt + TimeSpan.FromHours(1)).UtcDateTime,
        });

        return value;
    }
}
