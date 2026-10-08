using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Common;
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

    private const string Password = "urn:schemata:acr:classes:password";

    /// <summary>auth_time of the rotated session's own authentication: five minutes before the fixed clock.</summary>
    private static readonly long RotatedAuthTime = Anchor.AddMinutes(-5).ToUnixTimeSeconds();

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
        var idToken = pair.GetProperty("id_token").GetString();
        Assert.NotNull(idToken);
        var payload = Payload(idToken);

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

        var token       = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        var accessToken = token.GetProperty("access_token").GetString();
        Assert.NotNull(accessToken);
        var payload = Payload(accessToken);

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
        var idToken = pair.GetProperty("id_token").GetString();
        Assert.NotNull(idToken);
        var payload = Payload(idToken);

        Assert.False(payload.TryGetProperty(Claims.AuthTime, out var _));
        Assert.False(payload.TryGetProperty(Claims.Acr, out var _));
        Assert.False(payload.TryGetProperty(Claims.Amr, out var _));
    }

    [Fact]
    public async Task Mint_The_Stamped_Amr_Array_Without_A_Host_Provider() {
        using var factory = new WebAppFactory().WithEnvironment("Authenticated").WithServices(services => {
            services.AddAuthentication(SessionScheme)
                    .AddScheme<AuthenticationSchemeOptions, StampedSessionHandler>(SessionScheme, _ => { });
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(Anchor));
            services.RemoveAll<IAuthenticationContextProvider>();
        });
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new(SessionScheme);

        var code = await Approve(client);

        var response = await client.SendAsync(Token(code));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var pair    = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        var idToken = pair.GetProperty("id_token").GetString();
        Assert.NotNull(idToken);
        var payload = Payload(idToken);

        // The framework claim read must decode the JSON-typed amr claim as an array; the
        // scalar fallback would mint a single entry holding the JSON text itself.
        Assert.Equal(
            new[] { "pwd", "otp", "mfa" },
            payload.GetProperty(Claims.Amr).EnumerateArray().Select(entry => entry.GetString()));
        Assert.Equal(AuthTime, payload.GetProperty(Claims.AuthTime).GetInt64());
    }

    [Fact]
    public async Task Resolve_The_Approving_Event_When_The_Session_Changes_For_The_Same_Subject() {
        using var factory = New_Factory<RotatedSessionHandler>();
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new(SessionScheme);

        var code = await Approve(client);

        var response = await client.SendAsync(Token(code));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var pair    = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        var idToken = pair.GetProperty("id_token").GetString();
        Assert.NotNull(idToken);
        var payload = Payload(idToken);

        // The approval leg arrived on a rotated session: the multifactor event captured at
        // the authorize leg belongs to the superseded session, so the token carries the
        // approving session's own password-level evidence.
        Assert.Equal(Password, payload.GetProperty(Claims.Acr).GetString());
        Assert.Equal(RotatedAuthTime, payload.GetProperty(Claims.AuthTime).GetInt64());
        Assert.Equal(new[] { "pwd" }, payload.GetProperty(Claims.Amr).EnumerateArray().Select(entry => entry.GetString()));
    }

    [Fact]
    public async Task Resolve_The_Approving_Event_When_The_Host_Keeps_No_Session_Lineage() {
        using var factory = new WebAppFactory().WithEnvironment("AuthenticatedNoSession").WithServices(services => {
            services.AddAuthentication(SessionScheme)
                    .AddScheme<AuthenticationSchemeOptions, RotatedSessionHandler>(SessionScheme, _ => { });
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(Anchor));
        });
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new(SessionScheme);

        var code = await Approve(client, withMaxAge: false);

        var response = await client.SendAsync(Token(code));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var pair    = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        var idToken = pair.GetProperty("id_token").GetString();
        Assert.NotNull(idToken);
        var payload = Payload(idToken);

        // Without Session Management the authorize leg persists no OP session lineage, so the
        // approval on a different session cannot continue the captured multifactor event; the
        // token carries the approving session's own password-level evidence.
        Assert.Equal(Password, payload.GetProperty(Claims.Acr).GetString());
        Assert.Equal(RotatedAuthTime, payload.GetProperty(Claims.AuthTime).GetInt64());
        Assert.Equal(new[] { "pwd" }, payload.GetProperty(Claims.Amr).EnumerateArray().Select(entry => entry.GetString()));
    }

    [Fact]
    public async Task Preserve_The_Original_Event_Across_Code_Refresh_And_Introspection() {
        using var factory = new WebAppFactory().WithEnvironment("Authenticated").WithServices(services => {
            services.AddAuthentication(SessionScheme)
                    .AddScheme<AuthenticationSchemeOptions, StampedSessionHandler>(SessionScheme, _ => { });
            services.RemoveAll<IAuthenticationContextProvider>();
            services.AddSingleton<CountingContextProvider>();
            services.AddSingleton<IAuthenticationContextProvider>(sp => sp.GetRequiredService<CountingContextProvider>());
            services.PostConfigure<SchemataAuthorizationOptions>(o => o.AccessTokenFormat = TokenFormats.Jwt);
        });
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new(SessionScheme);

        var code = await Approve(client, withMaxAge: false, scope: $"{Scopes.OpenId} {Scopes.OfflineAccess}");
        var pair = await PostToken(client, [
            new("grant_type", GrantTypes.AuthorizationCode),
            new("client_id", "code-client"),
            new("client_secret", "code-secret"),
            new("code", code),
            new("redirect_uri", RedirectUri),
            new("code_verifier", Verifier),
        ]);
        var refresh = pair.GetProperty("refresh_token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(refresh));
        using (var scope = factory.Services.CreateScope()) {
            var store = scope.ServiceProvider.GetRequiredService<Schemata.Security.Skeleton.Services.ITokenStore<Schemata.Security.Skeleton.Entities.SchemataToken>>();
            var row = await store.FindByReferenceIdAsync(refresh);
            Assert.NotNull(row);
            var persisted = JsonSerializer.Deserialize<AuthorizationGrantContext>(row.GrantContext!, SchemataJson.Default);
            Assert.NotNull(persisted);
            Assert.Equal(Multifactor, persisted.Authentication?.Acr);
            Assert.Equal(AuthTime, persisted.Authentication?.AuthTime);
            Assert.Equal("users/u-1", persisted.Subject);
            Assert.Equal(TokenTypes.RefreshToken, row.Type);
            Assert.Equal(TokenStatuses.Valid, row.Status);
            Assert.True(row.ExpireTime > Anchor.UtcDateTime);
            Assert.Equal("users/u-1", row.Parent);
            var apps = scope.ServiceProvider.GetRequiredService<Skeleton.Managers.IApplicationManager<Skeleton.Entities.SchemataApplication>>();
            var app = await apps.FindByClientIdAsync("code-client");
            Assert.NotNull(app);
            Assert.Equal(app.CanonicalName, row.Application);
            var tokenService = scope.ServiceProvider.GetRequiredService<TokenService>();
            Assert.NotNull(await tokenService.Validate(row.Payload, lifetime: false));
        }

        var renewed = await PostToken(client, [
            new("grant_type", GrantTypes.RefreshToken),
            new("client_id", "code-client"),
            new("client_secret", "code-secret"),
            new("refresh_token", refresh),
        ]);
        var access = renewed.GetProperty("access_token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(access));
        var payload = Payload(access);
        Assert.Equal(AuthTime, payload.GetProperty(Claims.AuthTime).GetInt64());
        Assert.Equal(Multifactor, payload.GetProperty(Claims.Acr).GetString());

        using var introspection = await client.PostAsync("/connect/introspect", new FormUrlEncodedContent([
            new("client_id", "introspect-client"),
            new("client_secret", "introspect-secret"),
            new("token", access),
        ]));
        var introspectionBody = await introspection.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, introspection.StatusCode);
        var introspected = JsonDocument.Parse(introspectionBody).RootElement;
        Assert.Equal(AuthTime, introspected.GetProperty(Claims.AuthTime).GetInt64());
        Assert.Equal(Multifactor, introspected.GetProperty(Claims.Acr).GetString());

        Assert.Equal(1, factory.Services.GetRequiredService<CountingContextProvider>().Calls);
    }

    private static async Task<JsonElement> PostToken(
        HttpClient client,
        IEnumerable<KeyValuePair<string, string>> form
    ) {
        using var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(form));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(HttpStatusCode.OK == response.StatusCode, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    /// <summary>
    ///     Runs the interactive legs and returns the authorization code from the callback.
    ///     max_age rides along only when requested: without session evidence it makes
    ///     AdviceAuthorizePrompt demand re-authentication, which is not this test's subject.
    /// </summary>
    private static async Task<string> Approve(
        HttpClient client,
        bool withMaxAge = true,
        string scope = Scopes.OpenId
    ) {
        var url = "/connect/authorize?client_id=code-client"
                + "&redirect_uri=" + Uri.EscapeDataString(RedirectUri)
                + "&response_type=code&state=xyz"
                + (withMaxAge ? "&max_age=900" : string.Empty)
                + "&scope=" + Uri.EscapeDataString(scope)
                + "&code_challenge=" + Challenge
                + "&code_challenge_method=S256";

        var authorize = await client.GetAsync(url);
        Assert.True(HttpStatusCode.Found == authorize.StatusCode, await authorize.Content.ReadAsStringAsync());
        var location = authorize.Headers.Location;
        Assert.NotNull(location);
        Assert.True(
            location.IsAbsoluteUri
         && "https://localhost/interact" == location.GetLeftPart(UriPartial.Path),
            location.ToString());

        var interaction = HttpUtility.ParseQueryString(location.Query);
        var code     = interaction[Parameters.Code];
        var codeType = interaction[Parameters.CodeType];
        Assert.NotNull(code);
        Assert.NotNull(codeType);
        var approve = await client.PostAsync(
            "/connect/interact",
            new FormUrlEncodedContent(new Dictionary<string, string> {
                ["code"]      = code,
                ["code_type"] = codeType,
            }));
        var approveBody = await approve.Content.ReadAsStringAsync();
        Assert.True(HttpStatusCode.Found == approve.StatusCode, approveBody);
        var callbackLocation = approve.Headers.Location;
        Assert.NotNull(callbackLocation);
        var callback = HttpUtility.ParseQueryString(callbackLocation.Query);
        var callbackCode = callback[Parameters.Code];
        Assert.NotNull(callbackCode);
        return callbackCode;
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
    ///     Builds the session principal the Schemata.Identity login pipeline stamps: the amr
    ///     array as a JSON-typed claim and auth_time as an integer64-typed claim.
    /// </summary>
    private static ClaimsPrincipal StampedPrincipal() {
        return new(new ClaimsIdentity([
            new(IdentityClaims.Subject, "users/u-1"),
            new(Claims.Acr, Multifactor),
            new(Claims.Amr, """["pwd","otp","mfa"]""", JsonClaimValueTypes.Json),
            new(Claims.AuthTime, AuthTime.ToString(), ClaimValueTypes.Integer64),
        ], StampedSessionHandler.SchemeName));
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

            return Task.FromResult(AuthenticateResult.Success(new(StampedPrincipal(), SchemeName)));
        }
    }

    /// <summary>
    ///     Emulates a session rebind between the authorize and approval legs: the subject
    ///     persists, but the approval leg arrives on a rotated session identifier carrying
    ///     only the password-level evidence of the new authentication event.
    /// </summary>
    private sealed class RotatedSessionHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory                               logger,
        UrlEncoder                                   encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() {
            if (Request.Headers.Authorization != StampedSessionHandler.SchemeName) {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            if (!Request.Path.StartsWithSegments("/connect/interact")) {
                return Task.FromResult(
                    AuthenticateResult.Success(new(StampedPrincipal(), StampedSessionHandler.SchemeName)));
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity([
                new(IdentityClaims.Subject, "users/u-1"),
                new(Claims.SessionId, "sid-rotated"),
                new(Claims.Acr, Password),
                new(Claims.Amr, """["pwd"]""", JsonClaimValueTypes.Json),
                new(Claims.AuthTime, RotatedAuthTime.ToString(), ClaimValueTypes.Integer64),
            ], StampedSessionHandler.SchemeName));
            return Task.FromResult(
                AuthenticateResult.Success(new(principal, StampedSessionHandler.SchemeName)));
        }
    }

    private sealed class CountingContextProvider : IAuthenticationContextProvider
    {
        public int Calls { get; private set; }

        public Task<AuthenticationContext> GetContextAsync(ClaimsPrincipal? principal, CancellationToken ct = default) {
            Calls++;
            return Task.FromResult(Calls == 1
                ? new(Multifactor, ["pwd", "otp", "mfa"], AuthTime)
                : new AuthenticationContext("urn:example:drifted", ["sms"], AuthTime + 999));
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
