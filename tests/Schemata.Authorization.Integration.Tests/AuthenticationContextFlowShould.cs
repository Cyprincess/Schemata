using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.Tokens;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Integration.Tests;

[Trait("Category", "Integration")]
[Trait("Layer", "Component")]
public class AuthenticationContextFlowShould
{
    private const string RedirectUri = "https://localhost/callback";
    private const string Challenge   = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

    /// <summary>The RFC 7636 Appendix B verifier matching the challenge.</summary>
    private const string Verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";

    /// <summary>auth_time the session principal asserts: one minute before the fixed server clock, so a max_age request does not force re-authentication.</summary>
    private static readonly DateTimeOffset Anchor = new(2026, 8, 26, 0, 0, 0, TimeSpan.Zero);
    private static readonly long AuthTime = Anchor.AddMinutes(-1).ToUnixTimeSeconds();

    private const string Multifactor = "urn:schemata:acr:classes:multifactor";

    /// <summary>The scheme Program.cs wires into the authorization endpoint when authenticated.</summary>
    private const string SessionScheme = "ManagementTest";

    [Fact]
    public async Task Mint_The_Authentication_Context_Into_The_Exchanged_Id_Token() {
        using var factory = New_Factory<StampedSessionHandler>();
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new(SessionScheme);

        var code = await Approve(client);

        var response = await client.SendAsync(Token(code));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var pair    = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        var payload = Payload(pair.GetProperty("id_token").GetString()!);

        Assert.Equal(AuthTime, payload.GetProperty(Claims.AuthTime).GetInt64());
        Assert.Equal(Multifactor, payload.GetProperty(Claims.Acr).GetString());
        Assert.Equal(
            new[] { "pwd", "otp", "mfa" },
            payload.GetProperty(Claims.Amr).EnumerateArray().Select(entry => entry.GetString()));
    }

    [Fact]
    public async Task Mint_The_Authentication_Context_Into_The_Jwt_Access_Token() {
        using var factory = New_Factory<StampedSessionHandler>(TokenFormats.Jwt);
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new(SessionScheme);

        var code = await Approve(client);

        var response = await client.SendAsync(Token(code));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var token   = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        var payload = Payload(token.GetProperty("access_token").GetString()!);

        Assert.Equal(AuthTime, payload.GetProperty(Claims.AuthTime).GetInt64());
        Assert.Equal(Multifactor, payload.GetProperty(Claims.Acr).GetString());
        Assert.Equal(
            new[] { "pwd", "otp", "mfa" },
            payload.GetProperty(Claims.Amr).EnumerateArray().Select(entry => entry.GetString()));
    }

    [Fact]
    public async Task Mint_No_Context_Claims_When_The_Session_Carries_No_Evidence() {
        using var factory = New_Factory<PlainSessionHandler>();
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new(SessionScheme);

        var code = await Approve(client, withMaxAge: false);

        var response = await client.SendAsync(Token(code));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var pair    = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        var payload = Payload(pair.GetProperty("id_token").GetString()!);

        Assert.False(payload.TryGetProperty(Claims.AuthTime, out var _));
        Assert.False(payload.TryGetProperty(Claims.Acr, out var _));
        Assert.False(payload.TryGetProperty(Claims.Amr, out var _));
    }

    /// <summary>
    ///     Runs the interactive legs and returns the authorization code from the callback.
    ///     max_age rides along only when requested: without session evidence it makes
    ///     AdviceAuthorizePrompt demand re-authentication, which is not this test's subject.
    /// </summary>
    private static async Task<string> Approve(HttpClient client, bool withMaxAge = true) {
        var url = "/connect/authorize?client_id=code-client"
                + "&redirect_uri=" + Uri.EscapeDataString(RedirectUri)
                + "&response_type=code&state=xyz"
                + (withMaxAge ? "&max_age=900" : string.Empty)
                + "&scope=openid"
                + "&code_challenge=" + Challenge
                + "&code_challenge_method=S256";

        var authorize = await client.GetAsync(url);
        Assert.True(HttpStatusCode.Found == authorize.StatusCode, await authorize.Content.ReadAsStringAsync());
        Assert.True(
            authorize.Headers.Location!.IsAbsoluteUri
         && "https://localhost/interact" == authorize.Headers.Location.GetLeftPart(UriPartial.Path),
            authorize.Headers.Location!.ToString());

        var interaction = HttpUtility.ParseQueryString(authorize.Headers.Location!.Query);
        var approve = await client.PostAsync(
            "/connect/interact",
            new FormUrlEncodedContent(new Dictionary<string, string> {
                ["code"]      = interaction[Parameters.Code]!,
                ["code_type"] = interaction[Parameters.CodeType]!,
            }));
        var approveBody = await approve.Content.ReadAsStringAsync();
        Assert.True(HttpStatusCode.Found == approve.StatusCode, approveBody);
        var callback = HttpUtility.ParseQueryString(approve.Headers.Location!.Query);
        return callback[Parameters.Code]!;
    }

    private static HttpRequestMessage Token(string code) {
        return new(HttpMethod.Post, "/connect/token") {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> {
                ["grant_type"]    = GrantTypes.AuthorizationCode,
                ["client_id"]     = "code-client",
                ["client_secret"] = "code-secret",
                ["code"]          = code,
                ["redirect_uri"]  = RedirectUri,
                ["code_verifier"] = Verifier,
            }),
        };
    }

    /// <summary>Decodes a JWT payload segment into its raw JSON so scalar and array shapes are visible.</summary>
    private static JsonElement Payload(string jwt) {
        using var document = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(jwt.Split('.')[1]));
        return document.RootElement.Clone();
    }

    private static WebAppFactory New_Factory<THandler>(string? format = null)
        where THandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        return new WebAppFactory().WithEnvironment("Authenticated").WithServices(services => {
            services.AddAuthentication(SessionScheme)
                    .AddScheme<AuthenticationSchemeOptions, THandler>(SessionScheme, _ => { });
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(Anchor));
            if (format is not null) {
                services.PostConfigure<SchemataAuthorizationOptions>(o => o.AccessTokenFormat = format);
            }
        });
    }

    /// <summary>
    ///     Authenticates the resource owner for the interaction approval POST. The principal
    ///     emulates the claims the Schemata.Identity login pipeline stamps on a session.
    /// </summary>
    private sealed class StampedSessionHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory                               logger,
        UrlEncoder                                   encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "ManagementTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync() {
            if (Request.Headers.Authorization != SchemeName) {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity([
                new(IdentityClaims.Subject, "users/u-1"),
                new(Claims.Acr, Multifactor),
                new(Claims.Amr, """["pwd","otp","mfa"]"""),
                new(Claims.AuthTime, AuthTime.ToString()),
            ], SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new(principal, SchemeName)));
        }
    }

    /// <summary>Authenticates like a session that predates authentication-context stamping.</summary>
    private sealed class PlainSessionHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory                               logger,
        UrlEncoder                                   encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "ManagementTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync() {
            if (Request.Headers.Authorization != SchemeName) {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity([
                new(IdentityClaims.Subject, "users/u-1"),
            ], SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new(principal, SchemeName)));
        }
    }
}
