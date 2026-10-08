using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Integration.Tests;

[Trait("Category", "Integration")]
[Trait("Layer", "Component")]
public class LogoutHttpShould
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Current_Native_Host_Logout_Clears_Only_The_Target_And_Returns_State(bool expired) {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        using var factory = Factory(clock);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var hint = await IssueAsync(factory, clock);
        await LoginAsync(client, "sid-a");
        if (expired) clock.Advance(TimeSpan.FromHours(2));
        using var response = await client.GetAsync(Request(hint));
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("https://client.example/done?state=state%20%26%20value", response.Headers.Location!.OriginalString);
        using var session = JsonDocument.Parse(await client.GetStringAsync("/test/session"));
        Assert.Equal(JsonValueKind.Null, session.RootElement.GetProperty("subject").ValueKind);
        await AssertTargetsAsync(factory, retired: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cookieless_Hint_Preserves_Validated_Authority_For_Confirmation(bool expired) {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        using var factory = Factory(clock);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var hint = await IssueAsync(factory, clock);
        if (expired) clock.Advance(TimeSpan.FromHours(2));
        using var response = await client.GetAsync(Request(hint));
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/interact", response.Headers.Location!.AbsolutePath);
        await AssertTargetsAsync(factory, retired: false);
        await LoginAsync(client, "sid-a");
        using var approved = await ApproveAsync(client, response.Headers.Location);
        Assert.Equal(HttpStatusCode.Found, approved.StatusCode);
        Assert.Equal("https://client.example/done?state=state%20%26%20value", approved.Headers.Location!.OriginalString);
        await AssertTargetsAsync(factory, retired: true);
        using var replay = await ApproveAsync(client, response.Headers.Location);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
    }

    [Fact]
    public async Task Stale_Approval_Cannot_Clear_A_New_Host_Session_Or_Retire_Either_Target() {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        using var factory = Factory(clock);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var hint = await IssueAsync(factory, clock);
        await LoginAsync(client, "sid-b");
        using var response = await client.GetAsync(Request(hint));
        Assert.Equal("/interact", response.Headers.Location!.AbsolutePath);
        await LoginAsync(client, "sid-c");
        using var approved = await ApproveAsync(client, response.Headers.Location);
        Assert.Equal(HttpStatusCode.BadRequest, approved.StatusCode);
        using var session = JsonDocument.Parse(await client.GetStringAsync("/test/session"));
        Assert.Equal("sid-c", session.RootElement.GetProperty("session_id").GetString());
        await AssertTargetsAsync(factory, retired: false);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("wrong-client")]
    [InlineData("signature")]
    [InlineData("issuer")]
    public async Task BadHint_Has_No_Cookie_Online_Or_Participant_Effects(string kind) {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        using var factory = Factory(clock);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var hint = await IssueAsync(factory, clock);
        await LoginAsync(client, "sid-a");
        if (kind is "signature" or "issuer") {
            using var scope = factory.Services.CreateScope();
            var sp = scope.ServiceProvider;
            var issuer = sp.GetRequiredService<TokenService>();
            var options = sp.GetRequiredService<IOptions<SchemataAuthorizationOptions>>().Value;
            await using var signing = await issuer.BeginSigningAsync();
            using var rsa = System.Security.Cryptography.RSA.Create(2048);
            var credentials = kind == "signature"
                ? new Microsoft.IdentityModel.Tokens.SigningCredentials(new Microsoft.IdentityModel.Tokens.RsaSecurityKey(rsa), "RS256")
                : signing.Signing;
            if (kind == "issuer") options.Issuer = "https://foreign.example";
            hint = issuer.CreateToken(signing, credentials,
                [new("sub", "users/alice"), new("aud", "logout-client"), new("client_id", "logout-client"), new("sid", "sid-a")],
                clock.GetUtcNow(), clock.GetUtcNow().AddHours(1));
            options.Issuer = "https://localhost";
        }
        var request = kind == "invalid" ? Request("invalid") : kind == "wrong-client" ? Request(hint) + "&client_id=other" : Request(hint);
        using var response = await client.GetAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
        using var session = JsonDocument.Parse(await client.GetStringAsync("/test/session"));
        Assert.Equal("sid-a", session.RootElement.GetProperty("session_id").GetString());
        await AssertTargetsAsync(factory, retired: false);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NoHint_Get_And_Post_Require_Approval_Before_Clearing_The_Host(bool post) {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        using var factory = Factory(clock);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        await IssueAsync(factory, clock);
        await LoginAsync(client, "sid-a");
        using var response = post
            ? await client.PostAsync("/Connect/EndSession", new FormUrlEncodedContent(new Dictionary<string, string>()))
            : await client.GetAsync("/Connect/EndSession");
        Assert.Equal("/interact", response.Headers.Location!.AbsolutePath);
        await AssertTargetsAsync(factory, retired: false);
        using var approved = await ApproveAsync(client, response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        await AssertTargetsAsync(factory, retired: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Permanent_Only_Expired_Hint_Requires_Confirmation_Or_Structured_Failure(bool noInteraction) {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        using var factory = new WebAppFactory().WithEnvironment("EndSessionOnly").WithServices(services => {
            services.AddSingleton<TimeProvider>(clock);
            services.PostConfigure<SchemataAuthorizationOptions>(o => {
                o.IdTokenLifetime = TimeSpan.FromHours(1);
                if (noInteraction) o.InteractionUri = null;
            });
        });
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var hint = await IssueAsync(factory, clock);
        using (var scope = factory.Services.CreateScope()) {
            var tokens = scope.ServiceProvider.GetRequiredService<ITokenStore<SchemataToken>>();
            var credentials = new List<SchemataToken>();
            await foreach (var token in tokens.ListBySessionAsync("sid-a")) {
                if (token.Type != Schemata.Security.Skeleton.SecurityConstants.TokenTypes.SessionParticipant) credentials.Add(token);
            }
            foreach (var token in credentials) await tokens.RevokeAsync(token);
            await tokens.RemoveAsync("users/alice", "op-session", "sid-a");
        }
        clock.Advance(TimeSpan.FromHours(2));
        using var response = await client.GetAsync(Request(hint));
        if (noInteraction) {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(OAuthErrors.ServerError, body.RootElement.GetProperty("error").GetString());
            Assert.Null(response.Headers.Location);
        } else {
            Assert.Equal("/interact", response.Headers.Location!.AbsolutePath);
        }
        using var verify = factory.Services.CreateScope();
        var store = verify.ServiceProvider.GetRequiredService<ITokenStore<SchemataToken>>();
        Assert.Equal("applications/logout-client", Assert.Single(await store.ListParticipantsAsync("users/alice", "sid-a")));
        Assert.Equal("applications/logout-client", Assert.Single(await store.ListParticipantsAsync("users/alice", "sid-b")));
        Assert.NotNull(await store.GetAsync("users/alice", "op-session", "sid-b"));
    }

    [Fact]
    public async Task Foreign_User_Confirmation_Logs_Out_Only_The_Approving_Local_Target_Without_Rp_Redirect() {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        using var factory = Factory(clock);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var hint = await IssueAsync(factory, clock);
        using (var scope = factory.Services.CreateScope()) {
            var sp = scope.ServiceProvider;
            await sp.GetRequiredService<ITokenStore<SchemataToken>>().RegisterParticipantAsync("users/bob", "sid-bob", "applications/logout-client");
            await sp.GetRequiredService<IOpSessionService>().EstablishOnlineAsync("users/bob", "sid-bob");
        }
        await LoginAsync(client, "sid-bob", "users/bob");
        using var response = await client.GetAsync(Request(hint));
        Assert.Equal("/interact", response.Headers.Location!.AbsolutePath);
        using var approved = await ApproveAsync(client, response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Null(approved.Headers.Location);
        await AssertTargetsAsync(factory, retired: false);
        using var verify = factory.Services.CreateScope();
        var tokens = verify.ServiceProvider.GetRequiredService<ITokenStore<SchemataToken>>();
        Assert.Empty(await tokens.ListParticipantsAsync("users/bob", "sid-bob"));
        Assert.Null(await tokens.GetAsync("users/bob", "op-session", "sid-bob"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_Online_Credential_With_Host_Cookie_Logs_Out_Without_An_ActiveReader_Failure(bool expired) {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        using var factory = Factory(clock);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var hint = await IssueAsync(factory, clock, native: true);
        await LoginAsync(client, "sid-a");
        if (expired) clock.Advance(TimeSpan.FromHours(2));
        using var response = await client.GetAsync(Request(hint));
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("https://client.example/done?state=state%20%26%20value", response.Headers.Location!.OriginalString);
        using var session = JsonDocument.Parse(await client.GetStringAsync("/test/session"));
        Assert.Equal(JsonValueKind.Null, session.RootElement.GetProperty("subject").ValueKind);
        await AssertTargetsAsync(factory, retired: true);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task SIDless_Host_Confirmation_Freezes_The_Mirror_And_Rejects_A_Changed_Mirror(bool differentHint, bool stale) {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        using var factory = Factory(clock);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new("https://localhost") });
        var hint = await IssueAsync(factory, clock);
        await LoginAsync(client, "sid-b", sidless: true);
        using var pending = await client.GetAsync(differentHint ? Request(hint) : "/Connect/EndSession");
        Assert.Equal("/interact", pending.Headers.Location!.AbsolutePath);
        using (var saved = factory.Services.CreateScope()) {
            var code = HttpUtility.ParseQueryString(pending.Headers.Location.Query)["code"];
            var decision = await saved.ServiceProvider.GetRequiredService<ITokenStore<SchemataToken>>().FindByReferenceIdAsync(code);
            var payload = JsonSerializer.Deserialize<Schemata.Authorization.Foundation.Handlers.LogoutConfirmationPayload>(
                decision!.Payload!, saved.ServiceProvider.GetRequiredService<IOptions<JsonSerializerOptions>>().Value)!;
            Assert.Equal("users/alice", payload.Target.Subject);
            Assert.Equal("sid-b", payload.Target.SessionId);
        }
        if (stale) await LoginAsync(client, "sid-c", sidless: true);
        using var approved = await ApproveAsync(client, pending.Headers.Location);
        var expected = stale ? HttpStatusCode.BadRequest : differentHint ? HttpStatusCode.Found : HttpStatusCode.OK;
        Assert.True(approved.StatusCode == expected,
            $"Expected {expected}, received {approved.StatusCode}: {await approved.Content.ReadAsStringAsync()}");
        using var verify = factory.Services.CreateScope();
        var tokens = verify.ServiceProvider.GetRequiredService<ITokenStore<SchemataToken>>();
        Assert.Equal("applications/logout-client", Assert.Single(await tokens.ListParticipantsAsync("users/alice", "sid-a")));
        Assert.NotNull(await tokens.GetAsync("users/alice", "op-session", "sid-a"));
        if (stale) {
            Assert.Equal("applications/logout-client", Assert.Single(await tokens.ListParticipantsAsync("users/alice", "sid-b")));
            Assert.NotNull(await tokens.GetAsync("users/alice", "op-session", "sid-b"));
        } else {
            Assert.Empty(await tokens.ListParticipantsAsync("users/alice", "sid-b"));
            Assert.Null(await tokens.GetAsync("users/alice", "op-session", "sid-b"));
        }
        using var session = JsonDocument.Parse(await client.GetStringAsync("/test/session"));
        Assert.Equal(stale ? "users/alice" : null, session.RootElement.GetProperty("subject").GetString());
        using var replay = await ApproveAsync(client, pending.Headers.Location);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Subject_Only_Confirmation_Clears_Unchanged_Cookie_But_Rejects_New_Sid(bool changed) {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        using var factory = Factory(clock);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new("https://localhost") });
        using (var form = new FormUrlEncodedContent(new Dictionary<string, string> {
                   ["subject"] = "users/alice", ["sidless"] = "true", ["subject_only"] = "true" })) {
            using var login = await client.PostAsync("/test/login", form);
            Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        }
        using var pending = await client.GetAsync("/Connect/EndSession");
        Assert.Equal("/interact", pending.Headers.Location!.AbsolutePath);
        if (changed) await LoginAsync(client, "new-session");
        using var approved = await ApproveAsync(client, pending.Headers.Location);
        Assert.Equal(changed ? HttpStatusCode.BadRequest : HttpStatusCode.OK, approved.StatusCode);
        using var session = JsonDocument.Parse(await client.GetStringAsync("/test/session"));
        Assert.Equal(changed ? "users/alice" : null, session.RootElement.GetProperty("subject").GetString());
        if (changed) Assert.Equal("new-session", session.RootElement.GetProperty("session_id").GetString());
        using var replay = await ApproveAsync(client, pending.Headers.Location);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
    }

    private static WebAppFactory Factory(FakeTimeProvider clock) => new WebAppFactory().WithEnvironment("Logout").WithServices(services => {
        services.AddSingleton<TimeProvider>(clock);
        services.PostConfigure<SchemataAuthorizationOptions>(o => o.IdTokenLifetime = TimeSpan.FromHours(1));
    });

    private static async Task<string> IssueAsync(WebAppFactory factory, FakeTimeProvider clock, bool native = false) {
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        if (native) sp.GetRequiredService<IOptions<SchemataAuthorizationOptions>>().Value.AccessTokenLifetime = TimeSpan.FromDays(1);
        await sp.GetRequiredService<IApplicationManager<SchemataApplication>>().CreateAsync(new() {
            Name = "logout-client", ClientId = "logout-client", TokenEndpointAuthMethod = ClientAuthMethods.None,
            PostLogoutRedirectUris = ["https://client.example/done"], GrantTypes = [GrantTypes.AuthorizationCode],
            ResponseTypes = [ResponseTypes.Code], Scope = native ? "openid device_sso" : "openid",
            RedirectUris = ["https://localhost/callback"],
        });
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new("sub", "users/alice"), new("sid", "sid-a"), new("client_id", "logout-client")], "host"));
        var properties = new Dictionary<string, string?> {
            [Properties.GrantType] = GrantTypes.AuthorizationCode, [Properties.Scope] = native ? "openid device_sso" : "openid",
            [Properties.SessionId] = "sid-a", [Properties.GrantProfile] = GrantProfiles.OpenIdConnect,
        };
        var tokens = sp.GetRequiredService<ITokenStore<SchemataToken>>();
        var signIn = sp.GetRequiredService<IAuthorizationSignInService>();
        if (native) {
            properties[Properties.ResponseType] = ResponseTypes.Code;
            properties[Properties.RedirectUri] = "https://localhost/callback";
            var callback = await signIn.IssueAsync(principal, properties, AuthorizationSignInResponseKind.Callback);
            var code = (await tokens.FindByReferenceIdAsync(callback.Callback!.Parameters[Parameters.Code]))!;
            var grant = JsonSerializer.Deserialize<AuthorizationGrantContext>(code.GrantContext!, Schemata.Common.SchemataJson.Default)!;
            Assert.Equal(NativeSessionKinds.Online, grant.NativeSessionKind);
            Assert.NotNull(grant.OnlineSessionAuthority);
            properties[Properties.GrantContext] = code.GrantContext;
        }
        var issuance = await signIn.IssueAsync(principal, properties, AuthorizationSignInResponseKind.Token);
        Assert.NotNull(issuance.Token!.IdToken);
        if (!native) await tokens.CreateAsync(new() { Name = "long-lived", Type = TokenTypes.AccessToken, Status = TokenStatuses.Valid,
            Parent = "users/alice", SessionId = "sid-a", Application = "applications/logout-client", ExpireTime = clock.GetUtcNow().AddDays(1).UtcDateTime });
        await tokens.RegisterParticipantAsync("users/alice", "sid-b", "applications/logout-client");
        await sp.GetRequiredService<IOpSessionService>().EstablishOnlineAsync("users/alice", "sid-a");
        await sp.GetRequiredService<IOpSessionService>().EstablishOnlineAsync("users/alice", "sid-b");
        return issuance.Token.IdToken!;
    }

    private static async Task LoginAsync(HttpClient client, string sid, string subject = "users/alice", bool sidless = false) {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["subject"] = subject, ["sid"] = sid, ["sidless"] = sidless ? "true" : "false" });
        using var response = await client.PostAsync("/test/login", form);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
    private static string Request(string hint) => "/Connect/EndSession?id_token_hint=" + Uri.EscapeDataString(hint) + "&post_logout_redirect_uri=https%3A%2F%2Fclient.example%2Fdone&state=state%20%26%20value";
    private static Task<HttpResponseMessage> ApproveAsync(HttpClient client, Uri uri) {
        var query = HttpUtility.ParseQueryString(uri.Query);
        return client.PostAsync("/Connect/Interact", new FormUrlEncodedContent(new Dictionary<string, string> { ["code"] = query["code"]!, ["code_type"] = query["code_type"]! }));
    }
    private static async Task AssertTargetsAsync(WebAppFactory factory, bool retired) {
        using var scope = factory.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenStore<SchemataToken>>();
        Assert.Equal(retired, (await tokens.ListParticipantsAsync("users/alice", "sid-a")).Count == 0);
        Assert.Equal("applications/logout-client", Assert.Single(await tokens.ListParticipantsAsync("users/alice", "sid-b")));
        Assert.Equal(retired, await tokens.GetAsync("users/alice", "op-session", "sid-a") is null);
        Assert.NotNull(await tokens.GetAsync("users/alice", "op-session", "sid-b"));
    }
}
