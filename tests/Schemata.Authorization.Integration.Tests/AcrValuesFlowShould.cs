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
using Microsoft.IdentityModel.Tokens;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Integration.Tests;

[Trait("Category", "Integration")]
[Trait("Layer", "Component")]
public class AcrValuesFlowShould
{
    private const string RedirectUri = "https://localhost/callback";
    private const string Challenge   = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";
    private const string Verifier    = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
    private const string Multifactor = "urn:schemata:acr:classes:multifactor";

    /// <summary>A class no login of this deployment performs, so the request stays unsatisfied.</summary>
    private const string Unsatisfiable = "urn:example:acr:classes:hardware";

    /// <summary>The scheme Program.cs wires into the authorization endpoint when authenticated.</summary>
    private const string SessionScheme = "ManagementTest";

    private static readonly long AuthTime = new DateTimeOffset(2026, 8, 26, 0, 0, 0, TimeSpan.Zero).AddMinutes(-1).ToUnixTimeSeconds();

    [Fact]
    public async Task Echo_The_Requested_Acr_Values_To_The_Login_Interaction() {
        using var factory = New_Factory();
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new(SessionScheme);

        var url = AuthorizeUrl(acrValues: Multifactor);
        var authorize = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.Found, authorize.StatusCode);

        var interaction = HttpUtility.ParseQueryString(authorize.Headers.Location!.Query);
        var details     = await client.GetAsync(
            "/connect/interact"
          + "?code="      + HttpUtility.UrlEncode(interaction[Parameters.Code])
          + "&code_type=" + HttpUtility.UrlEncode(interaction[Parameters.CodeType]));

        Assert.Equal(HttpStatusCode.OK, details.StatusCode);

        var body = await details.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.TryGetProperty("request", out var request), body);
        Assert.True(request.TryGetProperty("acr_values", out var acr), body);
        Assert.Equal(Multifactor, acr.GetString());
    }

    [Fact]
    public async Task Mint_The_Satisfied_Requested_Class_Into_The_Id_Token() {
        using var factory = New_Factory();
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new(SessionScheme);

        var code = await Approve(client, Multifactor);
        var payload = await Exchange(client, code);

        Assert.Equal(Multifactor, payload.GetProperty(Claims.Acr).GetString());
    }

    [Fact]
    public async Task Mint_The_Performed_Class_When_The_Requested_Class_Is_Unsatisfiable() {
        using var factory = New_Factory();
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new(SessionScheme);

        // Core §5.5.1.1: the voluntary request the OP cannot satisfy proceeds, and the session's
        // current level — the multifactor class the login performed — becomes the acr claim.
        var code = await Approve(client, Unsatisfiable);
        var payload = await Exchange(client, code);

        Assert.Equal(Multifactor, payload.GetProperty(Claims.Acr).GetString());
    }

    [Fact]
    public async Task Advertise_Configured_Classes_In_Acr_Values_Supported() {
        using var factory = New_Factory(o => o.AcrValuesSupported.Add(Multifactor));
        var client = factory.CreateClient();

        var json = await Discovery(client);

        Assert.Equal(
            new[] { Multifactor },
            json.GetProperty("acr_values_supported").EnumerateArray().Select(v => v.GetString()));
    }

    [Fact]
    public async Task Omit_Acr_Values_Supported_When_No_Classes_Are_Configured() {
        using var factory = new WebAppFactory();
        var client = factory.CreateClient();

        var json = await Discovery(client);

        Assert.False(json.TryGetProperty("acr_values_supported", out var _));
    }

    private static async Task<JsonElement> Discovery(HttpClient client) {
        var response = await client.GetAsync("/.well-known/openid-configuration");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return document.RootElement.Clone();
    }

    private static string AuthorizeUrl(string? acrValues) {
        return "/connect/authorize?client_id=code-client"
             + "&redirect_uri=" + Uri.EscapeDataString(RedirectUri)
             + "&response_type=code&state=xyz"
             + "&scope=openid"
             + (acrValues is null ? string.Empty : "&acr_values=" + Uri.EscapeDataString(acrValues))
             + "&code_challenge=" + Challenge
             + "&code_challenge_method=S256";
    }

    private static async Task<string> Approve(HttpClient client, string acrValues) {
        var authorize = await client.GetAsync(AuthorizeUrl(acrValues));
        Assert.True(HttpStatusCode.Found == authorize.StatusCode, await authorize.Content.ReadAsStringAsync());

        var interaction = HttpUtility.ParseQueryString(authorize.Headers.Location!.Query);
        var approve = await client.PostAsync(
            "/connect/interact",
            new FormUrlEncodedContent(new Dictionary<string, string> {
                ["code"]      = interaction[Parameters.Code]!,
                ["code_type"] = interaction[Parameters.CodeType]!,
            }));
        Assert.True(HttpStatusCode.Found == approve.StatusCode, await approve.Content.ReadAsStringAsync());

        var callback = HttpUtility.ParseQueryString(approve.Headers.Location!.Query);
        return callback[Parameters.Code]!;
    }

    private static async Task<JsonElement> Exchange(HttpClient client, string code) {
        var response = await client.SendAsync(new(HttpMethod.Post, "/connect/token") {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> {
                ["grant_type"]    = GrantTypes.AuthorizationCode,
                ["client_id"]     = "code-client",
                ["client_secret"] = "code-secret",
                ["code"]          = code,
                ["redirect_uri"]  = RedirectUri,
                ["code_verifier"] = Verifier,
            }),
        });
        Assert.True(HttpStatusCode.OK == response.StatusCode, await response.Content.ReadAsStringAsync());

        var pair = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        return Payload(pair.GetProperty("id_token").GetString()!);
    }

    /// <summary>Decodes a JWT payload segment into its raw JSON so scalar and array shapes are visible.</summary>
    private static JsonElement Payload(string jwt) {
        using var document = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(jwt.Split('.')[1]));
        return document.RootElement.Clone();
    }

    private static WebAppFactory New_Factory(Action<SchemataAuthorizationOptions>? configure = null) {
        return new WebAppFactory().WithEnvironment("Authenticated").WithServices(services => {
            services.AddAuthentication(SessionScheme)
                    .AddScheme<AuthenticationSchemeOptions, StampedSessionHandler>(SessionScheme, _ => { });
            if (configure is not null) {
                services.PostConfigure(configure);
            }
        });
    }

    /// <summary>
    ///     Authenticates the resource owner for the interaction approval POST. The principal
    ///     emulates the claims the Schemata.Identity login pipeline stamps on a session whose
    ///     authentication satisfied the multifactor class.
    /// </summary>
    private sealed class StampedSessionHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory                               logger,
        UrlEncoder                                   encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() {
            if (Request.Headers.Authorization != SessionScheme) {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity([
                new(IdentityClaims.Subject, "users/u-1"),
                new(Claims.Acr, Multifactor),
                new(Claims.Amr, """["pwd","otp","mfa"]"""),
                new(Claims.AuthTime, AuthTime.ToString()),
            ], SessionScheme));
            return Task.FromResult(AuthenticateResult.Success(new(principal, SessionScheme)));
        }
    }
}
