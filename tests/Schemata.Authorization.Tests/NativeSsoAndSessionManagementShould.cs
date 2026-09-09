using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Advice;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Filters;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Foundation.Managers;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Entity.Repository;
using Schemata.Security.Foundation.Stores;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class DeviceSsoShould
{
    private const string Issuer = "https://issuer.example";
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Issue_A_Bound_Device_Secret_For_An_Eligible_Code_Exchange() {
        var app     = NativeApplication("native-client");
        var apps    = new Mock<IApplicationManager<SchemataApplication>>();
        apps.Setup(a => a.HasPermissionAsync(app, PermissionPrefixes.Scope + Scopes.DeviceSso,
                                              It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var tokens  = NewTokenStore();
        var devices = new Mock<IDeviceIdResolver>();
        devices.Setup(d => d.ResolveAsync(null, null, It.IsAny<CancellationToken>())).ReturnsAsync("device-1");
        SchemataToken? created = null;
        tokens.Setup(t => t.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()))
              .Callback((SchemataToken token, CancellationToken _) => created = token)
              .ReturnsAsync((SchemataToken token, CancellationToken _) => token);
        var advisor = new AdviceCodeExchangeDeviceSecret<SchemataApplication>(
            apps.Object, tokens.Object, devices.Object, NativeOptions(), FixedTime());
        using var provider = new ServiceCollection().BuildServiceProvider();
        var ctx = new AdviceContext(provider);

        await advisor.AdviseAsync(ctx, CodeExchange(app, $"{Scopes.OpenId} {Scopes.DeviceSso}", "sid-1"));

        Assert.NotNull(created);
        Assert.Equal(TokenTypes.DeviceSecret, created.Type);
        Assert.Equal(TokenStatuses.Valid, created.Status);
        Assert.Equal(TokenFormats.Reference, created.Format);
        Assert.Equal(app.CanonicalName, created.Application);
        Assert.Equal("sid-1", created.SessionId);
        Assert.Equal("device-1", created.DeviceId);
        Assert.Equal(Now.UtcDateTime.AddDays(14), created.ExpireTime);
        Assert.False(string.IsNullOrWhiteSpace(created.ReferenceId));
        Assert.True(ctx.TryGet<DeviceSecretIssuance>(out var issuance));
        Assert.Equal(created.ReferenceId, issuance!.DeviceSecret);
        Assert.Equal("device-1", issuance.DeviceId);
        Assert.Equal(app.CanonicalName, issuance.SourceClientId);
        Assert.Equal("sid-1", issuance.SessionId);
    }

    [Fact]
    public async Task Reject_Code_Exchange_Device_Secret_Issuance_Without_A_Session() {
        var app     = NativeApplication("native-client");
        var tokens  = NewTokenStore();
        var advisor = new AdviceCodeExchangeDeviceSecret<SchemataApplication>(
            ApplicationManager(),
            tokens.Object,
            new NoDeviceIdResolver(),
            NativeOptions(),
            FixedTime());
        using var provider = new ServiceCollection().BuildServiceProvider();

        var ex = await Assert.ThrowsAsync<OAuthException>(() => advisor.AdviseAsync(
            new AdviceContext(provider), CodeExchange(app, $"{Scopes.OpenId} {Scopes.DeviceSso}", null)));

        Assert.Equal(OAuthErrors.InvalidGrant, ex.Status);
        tokens.Verify(t => t.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Omit_Device_Secret_When_Device_Sso_Is_Not_Requested() {
        var app     = NativeApplication("native-client");
        var tokens  = NewTokenStore();
        var devices = new Mock<IDeviceIdResolver>();
        var advisor = new AdviceCodeExchangeDeviceSecret<SchemataApplication>(
            ApplicationManager(), tokens.Object, devices.Object, NativeOptions(), FixedTime());
        using var provider = new ServiceCollection().BuildServiceProvider();
        var ctx = new AdviceContext(provider);

        await advisor.AdviseAsync(ctx, CodeExchange(app, Scopes.OpenId, "sid-1"));

        Assert.False(ctx.TryGet<DeviceSecretIssuance>(out _));
        tokens.Verify(t => t.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()), Times.Never);
        devices.Verify(d => d.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<string?>(),
                                           It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reject_Device_Sso_Without_The_Openid_Scope() {
        var app = NativeApplication("native-client");
        var advisor = new AdviceCodeExchangeDeviceSecret<SchemataApplication>(
            ApplicationManager(), NewTokenStore().Object, new NoDeviceIdResolver(), NativeOptions(), FixedTime());
        using var provider = new ServiceCollection().BuildServiceProvider();

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            advisor.AdviseAsync(new AdviceContext(provider), CodeExchange(app, Scopes.DeviceSso, "sid-1")));

        Assert.Equal(OAuthErrors.InvalidScope, exception.Status);
    }

    [Fact]
    public async Task Reject_Device_Sso_When_The_Client_Lacks_Permission() {
        var app  = NativeApplication("native-client");
        var apps = new Mock<IApplicationManager<SchemataApplication>>();
        apps.Setup(a => a.HasPermissionAsync(app, PermissionPrefixes.Scope + Scopes.DeviceSso,
                                              It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var tokens = NewTokenStore();
        var advisor = new AdviceCodeExchangeDeviceSecret<SchemataApplication>(
            apps.Object, tokens.Object, new NoDeviceIdResolver(), NativeOptions(), FixedTime());
        using var provider = new ServiceCollection().BuildServiceProvider();

        var exception = await Assert.ThrowsAsync<OAuthException>(() => advisor.AdviseAsync(
            new AdviceContext(provider), CodeExchange(app, $"{Scopes.OpenId} {Scopes.DeviceSso}", "sid-1")));

        Assert.Equal(OAuthErrors.InvalidScope, exception.Status);
        tokens.Verify(t => t.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reuse_A_Valid_Supplied_Device_Secret_For_The_Same_Client_And_Session() {
        var app      = NativeApplication("native-client");
        var existing = DeviceSecret("secret-1", app.CanonicalName!, "sid-1", "device-old");
        var tokens   = NewTokenStore();
        tokens.Setup(t => t.FindByReferenceIdAsync("secret-1", It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        var advisor = new AdviceCodeExchangeDeviceSecret<SchemataApplication>(
            ApplicationManager(),
            tokens.Object,
            new NoDeviceIdResolver(),
            NativeOptions(),
            FixedTime());
        using var provider = new ServiceCollection().BuildServiceProvider();
        var ctx = new AdviceContext(provider);
        var exchange = CodeExchange(app, $"{Scopes.OpenId} {Scopes.DeviceSso}", "sid-1");
        exchange.Request!.DeviceSecret = "secret-1";

        await advisor.AdviseAsync(ctx, exchange);

        Assert.True(ctx.TryGet<DeviceSecretIssuance>(out var issuance));
        Assert.Equal("secret-1", issuance!.DeviceSecret);
        Assert.Equal("device-old", issuance.DeviceId);
        tokens.Verify(t => t.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("foreign-client")]
    [InlineData("foreign-session")]
    [InlineData("expired")]
    [InlineData("revoked")]
    public async Task Replace_An_Unusable_Supplied_Device_Secret(string reason) {
        var app      = NativeApplication("native-client");
        var existing = DeviceSecret("secret-old", app.CanonicalName!, "sid-1", "device-old");
        switch (reason) {
            case "foreign-client":
                existing.Application = "applications/other-client";
                break;
            case "foreign-session":
                existing.SessionId = "sid-other";
                break;
            case "expired":
                existing.ExpireTime = Now.UtcDateTime;
                break;
            case "revoked":
                existing.Status = TokenStatuses.Revoked;
                break;
        }
        var tokens = NewTokenStore();
        tokens.Setup(t => t.FindByReferenceIdAsync("secret-old", It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        SchemataToken? created = null;
        tokens.Setup(t => t.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()))
              .Callback((SchemataToken token, CancellationToken _) => created = token)
              .ReturnsAsync((SchemataToken token, CancellationToken _) => token);
        var devices = new Mock<IDeviceIdResolver>();
        devices.Setup(d => d.ResolveAsync(null, null, It.IsAny<CancellationToken>())).ReturnsAsync("device-new");
        var advisor = new AdviceCodeExchangeDeviceSecret<SchemataApplication>(
            ApplicationManager(), tokens.Object, devices.Object, NativeOptions(), FixedTime());
        using var provider = new ServiceCollection().BuildServiceProvider();
        var ctx = new AdviceContext(provider);
        var exchange = CodeExchange(app, $"{Scopes.OpenId} {Scopes.DeviceSso}", "sid-1");
        exchange.Request!.DeviceSecret = "secret-old";

        await advisor.AdviseAsync(ctx, exchange);

        Assert.NotNull(created);
        Assert.NotEqual("secret-old", created.ReferenceId);
        Assert.Equal(app.CanonicalName, created.Application);
        Assert.Equal("sid-1", created.SessionId);
        Assert.Equal("device-new", created.DeviceId);
        Assert.True(ctx.TryGet<DeviceSecretIssuance>(out var issuance));
        Assert.Equal(created.ReferenceId, issuance!.DeviceSecret);
    }

    [Fact]
    public async Task Reuse_An_Existing_Bound_Device_Secret_During_Refresh() {
        var app      = NativeApplication("native-client");
        var existing = DeviceSecret("secret-1", app.CanonicalName!, "sid-1", "device-1");
        var tokens   = NewTokenStore();
        tokens.Setup(t => t.ListByParentAsync(app.CanonicalName, TokenTypes.DeviceSecret,
                                              It.IsAny<CancellationToken>()))
              .Returns(Enumerate(existing));
        var devices = new Mock<IDeviceIdResolver>();
        devices.Setup(d => d.ResolveAsync(null, null, It.IsAny<CancellationToken>())).ReturnsAsync("device-1");
        var advisor = new AdviceRefreshTokenDeviceSecret<SchemataApplication>(
            ApplicationManager(), tokens.Object, devices.Object, NativeOptions(), FixedTime());
        using var provider = new ServiceCollection().BuildServiceProvider();
        var ctx = new AdviceContext(provider);

        await advisor.AdviseAsync(ctx, RefreshExchange(app, "sid-1"));

        Assert.True(ctx.TryGet<DeviceSecretIssuance>(out var issuance));
        Assert.Equal("secret-1", issuance!.DeviceSecret);
        tokens.Verify(t => t.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Issue_One_Bound_Device_Secret_When_Refresh_Has_None() {
        var app    = NativeApplication("native-client");
        var tokens = NewTokenStore();
        tokens.Setup(t => t.ListByParentAsync(app.CanonicalName, TokenTypes.DeviceSecret,
                                              It.IsAny<CancellationToken>()))
              .Returns(Enumerate<SchemataToken>());
        SchemataToken? created = null;
        tokens.Setup(t => t.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()))
              .Callback((SchemataToken token, CancellationToken _) => created = token)
              .ReturnsAsync((SchemataToken token, CancellationToken _) => token);
        var devices = new Mock<IDeviceIdResolver>();
        devices.Setup(d => d.ResolveAsync(null, null, It.IsAny<CancellationToken>())).ReturnsAsync("device-1");
        var advisor = new AdviceRefreshTokenDeviceSecret<SchemataApplication>(
            ApplicationManager(), tokens.Object, devices.Object, NativeOptions(), FixedTime());
        using var provider = new ServiceCollection().BuildServiceProvider();
        var ctx = new AdviceContext(provider);

        await advisor.AdviseAsync(ctx, RefreshExchange(app, "sid-1"));

        Assert.NotNull(created);
        Assert.Equal(app.CanonicalName, created.Application);
        Assert.Equal("sid-1", created.SessionId);
        Assert.True(ctx.TryGet<DeviceSecretIssuance>(out var issuance));
        Assert.Equal(created.ReferenceId, issuance!.DeviceSecret);
        tokens.Verify(t => t.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Return_Device_Secret_Only_With_An_Id_Token_And_Bind_Its_Hash_And_Session() {
        var options = new SchemataAuthorizationOptions { Issuer = Issuer, AccessTokenFormat = TokenFormats.Jwt };
        var issuer  = TestSecurityKeys.CreateTokenService(options, time: FixedTime());
        var app     = NativeApplication("native-client");
        var apps    = new Mock<IApplicationManager<SchemataApplication>>();
        apps.Setup(a => a.FindByClientIdAsync(app.ClientId, It.IsAny<CancellationToken>())).ReturnsAsync(app);
        var tokens = NewTokenStore();
        tokens.Setup(t => t.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((SchemataToken token, CancellationToken _) => token);
        using var provider = new ServiceCollection()
                            .AddSingleton<IClaimsAdvisor>(new AdviceClaimsAudience(Options.Create(options)))
                            .AddSingleton<IDestinationAdvisor>(new AdviceDestinationSubject())
                            .BuildServiceProvider();
        var service = new AuthorizationSignInService<SchemataApplication>(
            Options.Create(options), Options.Create(new JsonSerializerOptions()), issuer,
            apps.Object, tokens.Object, provider, FixedTime());
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(IdentityClaims.Subject, "user-1"),
            new(Claims.ClientId, app.ClientId!),
        ], "grant"));
        var ctx = new AdviceContext(provider);
        ctx.Set(new DeviceSecretIssuance("device-secret-1", "device-1", app.CanonicalName, "sid-1"));
        using var ambient = AdviceContext.Establish(ctx);

        var issued = await service.IssueAsync(principal, new Dictionary<string, string?> {
            [Properties.GrantType] = GrantTypes.AuthorizationCode,
            [Properties.Scope]     = $"{Scopes.OpenId} {Scopes.DeviceSso}",
            [Properties.SessionId] = "sid-1",
        }, AuthorizationSignInResponseKind.Token);

        Assert.NotNull(issued.Token);
        Assert.Equal("device-secret-1", issued.Token.DeviceSecret);
        Assert.NotNull(issued.Token.IdToken);
        var idToken = new JsonWebTokenHandler().ReadJsonWebToken(issued.Token.IdToken);
        var signing = await issuer.ResolveSigningCredentials();
        Assert.True(idToken.TryGetPayloadValue<string>(Claims.DsHash, out var dsHash));
        Assert.True(idToken.TryGetPayloadValue<string>(Claims.SessionId, out var sessionId));
        Assert.Equal(TokenService.ComputeHash("device-secret-1", signing), dsHash);
        Assert.Equal("sid-1", sessionId);

        var withoutOpenId = await service.IssueAsync(
            new ClaimsPrincipal(new ClaimsIdentity([new(IdentityClaims.Subject, "user-2")], "grant")),
            new Dictionary<string, string?> {
                [Properties.GrantType] = GrantTypes.AuthorizationCode,
                [Properties.Scope]     = Scopes.DeviceSso,
            },
            AuthorizationSignInResponseKind.Token);
        Assert.NotNull(withoutOpenId.Token);
        Assert.Null(withoutOpenId.Token.IdToken);
        Assert.Null(withoutOpenId.Token.DeviceSecret);
    }

    [Fact]
    public async Task Exchange_A_Valid_Native_Sso_Profile_For_A_Bearer_Access_Token() {
        var fixture = await NativeExchangeFixture();

        var result = await fixture.Handler.HandleAsync(fixture.Target, fixture.Request, null, CancellationToken.None);

        var response = Assert.IsType<TokenResponse>(result.Data);
        Assert.Equal(Schemes.Bearer, response.TokenType);
        Assert.Equal(TokenTypeUris.AccessToken, response.IssuedTokenType);
        Assert.False(string.IsNullOrWhiteSpace(response.AccessToken));
        Assert.Equal("openid profile", response.Scope);
        var created = Assert.Single(fixture.Created);
        Assert.Equal(response.AccessToken, created.ReferenceId);
        Assert.Equal(fixture.Target.CanonicalName, created.Application);
        Assert.Equal("sid-1", created.SessionId);
        Assert.Equal("user-1", created.Parent);
    }

    [Fact]
    public async Task Reject_Native_Sso_Exchange_With_An_Unknown_Device_Secret() {
        var fixture = await NativeExchangeFixture();
        fixture.Tokens.Setup(t => t.FindByReferenceIdAsync("device-secret-1", It.IsAny<CancellationToken>()))
               .ReturnsAsync((SchemataToken?)null);

        var ex = await Assert.ThrowsAsync<OAuthException>(() => fixture.Handler.HandleAsync(
            fixture.Target, fixture.Request, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidGrant, ex.Status);
        Assert.Empty(fixture.Created);
    }

    [Fact]
    public async Task Reject_Native_Sso_Exchange_When_The_Id_Token_Signature_Is_Invalid() {
        var fixture      = await NativeExchangeFixture();
        var foreignIssuer = TestSecurityKeys.CreateTokenService(
            new SchemataAuthorizationOptions { Issuer = Issuer }, time: FixedTime());
        fixture.Request.SubjectToken = await foreignIssuer.CreateToken([
            new(Claims.Audience, fixture.Source.ClientId!),
            new(Claims.DsHash, "irrelevant"),
            new(Claims.SessionId, "sid-1"),
        ], TimeSpan.FromHours(1));

        var ex = await Assert.ThrowsAsync<OAuthException>(() => fixture.Handler.HandleAsync(
            fixture.Target, fixture.Request, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidGrant, ex.Status);
        Assert.Empty(fixture.Created);
    }

    [Fact]
    public async Task Reject_Native_Sso_Exchange_When_The_Device_Secret_Hash_Does_Not_Match() {
        var fixture = await NativeExchangeFixture(dsHash: "wrong-hash");

        var ex = await Assert.ThrowsAsync<OAuthException>(() => fixture.Handler.HandleAsync(
            fixture.Target, fixture.Request, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidGrant, ex.Status);
        Assert.Empty(fixture.Created);
    }

    [Fact]
    public async Task Reject_Native_Sso_Exchange_When_The_Session_Has_No_Live_Token() {
        var fixture = await NativeExchangeFixture();
        fixture.Tokens.Setup(t => t.ListBySessionAsync("sid-1", It.IsAny<CancellationToken>()))
               .Returns(Enumerate(new SchemataToken {
                   Type = TokenTypes.AccessToken, Status = TokenStatuses.Revoked, SessionId = "sid-1",
               }));

        var ex = await Assert.ThrowsAsync<OAuthException>(() => fixture.Handler.HandleAsync(
            fixture.Target, fixture.Request, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidGrant, ex.Status);
        Assert.Empty(fixture.Created);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Reject_Native_Sso_Exchange_When_Either_Client_Lacks_Permission(
        bool sourcePermitted,
        bool targetPermitted
    ) {
        var fixture = await NativeExchangeFixture(sourcePermitted: sourcePermitted, targetPermitted: targetPermitted);

        var ex = await Assert.ThrowsAsync<OAuthException>(() => fixture.Handler.HandleAsync(
            fixture.Target, fixture.Request, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.UnauthorizedClient, ex.Status);
        Assert.Empty(fixture.Created);
    }

    [Fact]
    public async Task Require_Interaction_For_A_Scope_Not_Granted_To_The_Source_Client() {
        var fixture = await NativeExchangeFixture();
        fixture.Request.Scope = "openid email";

        var ex = await Assert.ThrowsAsync<OAuthException>(() => fixture.Handler.HandleAsync(
            fixture.Target, fixture.Request, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InteractionRequired, ex.Status);
        Assert.Empty(fixture.Created);
    }

    [Fact]
    public async Task Route_Token_Exchange_To_The_Composite_Registration_Before_The_Subject_Fallback() {
        var target = NativeApplication("target-client");
        var apps   = new Mock<IApplicationManager<SchemataApplication>>();
        var tokens = NewTokenStore();
        tokens.Setup(t => t.FindByReferenceIdAsync("missing-secret", It.IsAny<CancellationToken>()))
              .ReturnsAsync((SchemataToken?)null);
        var native = new NativeSsoTokenExchangeHandler<SchemataApplication>(
            tokens.Object,
            TestSecurityKeys.CreateTokenService(new SchemataAuthorizationOptions { Issuer = Issuer }, time: FixedTime()),
            apps.Object,
            ServerOptions(),
            FixedTime());
        var fallback = new Mock<ITokenExchangeHandler<SchemataApplication>>();
        fallback.Setup(f => f.HandleAsync(It.IsAny<SchemataApplication>(), It.IsAny<TokenRequest>(),
                                           It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("fallback selected"));
        var authenticator = new Mock<IClientAuthentication<SchemataApplication>>();
        authenticator.SetupGet(a => a.Method).Returns(ClientAuthMethods.ClientSecretPost);
        authenticator.Setup(a => a.AuthenticateAsync(null, It.IsAny<Dictionary<string, List<string?>>?>(), null,
                                                       It.IsAny<CancellationToken>()))
                     .ReturnsAsync(target);
        var auth = new ClientAuthenticationService<SchemataApplication>([authenticator.Object]);
        using var provider = new ServiceCollection()
                            .AddKeyedSingleton<ITokenExchangeHandler<SchemataApplication>>(
                                $"{TokenTypeUris.IdToken}|{TokenTypeUris.AccessToken}", native)
                            .AddKeyedSingleton(TokenTypeUris.IdToken, fallback.Object)
                            .BuildServiceProvider();
        var handler = new TokenExchangeHandler<SchemataApplication>(auth, provider);
        using var ambient = AdviceContext.Establish(new(provider));
        var request = new TokenRequest {
            GrantType          = GrantTypes.TokenExchange,
            ClientId          = target.ClientId,
            SubjectToken      = "signed-id-token",
            SubjectTokenType  = TokenTypeUris.IdToken,
            ActorToken        = "missing-secret",
            ActorTokenType    = TokenTypeUris.DeviceSecret,
            RequestedTokenType = TokenTypeUris.AccessToken,
            Audience          = Issuer,
        };

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.HandleAsync(request, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidGrant, ex.Status);
        fallback.Verify(f => f.HandleAsync(It.IsAny<SchemataApplication>(), It.IsAny<TokenRequest>(),
                                            It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Revoke_Only_Live_Tokens_Bound_To_The_Selected_Device() {
        var matching = new SchemataToken { DeviceId = "device-1", Status = TokenStatuses.Valid };
        var other    = new SchemataToken { DeviceId = "device-2", Status = TokenStatuses.Valid };
        var revoked  = new SchemataToken { DeviceId = "device-1", Status = TokenStatuses.Revoked };
        var rows       = new[] { matching, other, revoked };
        var repository = new Mock<IRepository<SchemataToken>>();
        repository.Setup(r => r.ListAsync(
                       It.IsAny<Func<IQueryable<SchemataToken>, IQueryable<SchemataToken>>>(),
                       It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<SchemataToken>, IQueryable<SchemataToken>> query, CancellationToken _) =>
                      Enumerate(query(rows.AsQueryable()).ToArray()));
        var store = new RepositoryTokenStore(repository.Object, FixedTime());

        var count = await store.RevokeByDeviceAsync("device-1");

        Assert.Equal(1, count);
        Assert.Equal(TokenStatuses.Revoked, matching.Status);
        Assert.Equal(TokenStatuses.Valid, other.Status);
        repository.Verify(r => r.UpdateAsync(matching, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.UpdateAsync(other, It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(r => r.UpdateAsync(revoked, It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static SchemataApplication NativeApplication(string clientId) {
        return new() {
            Uid           = Guid.NewGuid(),
            ClientId      = clientId,
            CanonicalName = $"applications/{clientId}",
            Permissions   = [
                PermissionPrefixes.Scope + Scopes.DeviceSso,
                PermissionPrefixes.Scope + Scopes.OpenId,
                PermissionPrefixes.Scope + "profile",
            ],
        };
    }

    private static IApplicationManager<SchemataApplication> ApplicationManager() {
        return new SchemataApplicationManager<SchemataApplication>(new Mock<IRepository<SchemataApplication>>().Object);
    }

    private static Mock<ITokenStore<SchemataToken>> NewTokenStore() {
        return new(MockBehavior.Loose);
    }

    private static SchemataToken DeviceSecret(string reference, string application, string sid, string device) {
        return new() {
            Type        = TokenTypes.DeviceSecret,
            Status      = TokenStatuses.Valid,
            Format      = TokenFormats.Reference,
            ReferenceId = reference,
            Application = application,
            SessionId   = sid,
            DeviceId    = device,
            ExpireTime  = Now.UtcDateTime.AddHours(1),
        };
    }

    private static CodeExchangeContext<SchemataApplication> CodeExchange(
        SchemataApplication app,
        string              scope,
        string?             sid
    ) {
        return new() {
            Application = app,
            Request     = new TokenRequest { Scope = scope },
            Payload     = new AuthorizeRequest { Scope = scope },
            CodeToken   = new SchemataToken { SessionId = sid },
        };
    }

    private static RefreshTokenContext<SchemataApplication> RefreshExchange(SchemataApplication app, string sid) {
        return new() {
            Application = app,
            Request     = new TokenRequest { Scope = $"{Scopes.OpenId} {Scopes.DeviceSso}" },
            Token       = new SchemataToken { SessionId = sid },
        };
    }

    private static IOptions<NativeSingleSignOnOptions> NativeOptions() {
        return Options.Create(new NativeSingleSignOnOptions());
    }

    private static IOptions<SchemataAuthorizationOptions> ServerOptions() {
        return Options.Create(new SchemataAuthorizationOptions { Issuer = Issuer });
    }

    private static TimeProvider FixedTime() {
        var time = new Mock<TimeProvider>();
        time.Setup(t => t.GetUtcNow()).Returns(Now);
        return time.Object;
    }

    private static async Task<NativeFixture> NativeExchangeFixture(
        string? dsHash = null,
        bool sourcePermitted = true,
        bool targetPermitted = true
    ) {
        var options = new SchemataAuthorizationOptions { Issuer = Issuer };
        var issuer  = TestSecurityKeys.CreateTokenService(options, time: FixedTime());
        var source  = NativeApplication("source-client");
        var target  = NativeApplication("target-client");
        var secret  = DeviceSecret("device-secret-1", source.CanonicalName!, "sid-1", "device-1");
        var signing = await issuer.ResolveSigningCredentials();
        var idToken = await issuer.CreateToken([
            new(IdentityClaims.Subject, "user-1"),
            new(Claims.Audience, source.ClientId!),
            new(Claims.DsHash, dsHash ?? TokenService.ComputeHash(secret.ReferenceId!, signing)),
            new(Claims.SessionId, "sid-1"),
        ], TimeSpan.FromHours(1));
        var apps = new Mock<IApplicationManager<SchemataApplication>>();
        apps.Setup(a => a.FindByClientIdAsync(source.ClientId, It.IsAny<CancellationToken>())).ReturnsAsync(source);
        apps.Setup(a => a.HasPermissionAsync(source, PermissionPrefixes.Scope + Scopes.DeviceSso,
                                              It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourcePermitted);
        apps.Setup(a => a.HasPermissionAsync(target, PermissionPrefixes.Scope + Scopes.DeviceSso,
                                              It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetPermitted);
        var tokens = NewTokenStore();
        tokens.Setup(t => t.FindByReferenceIdAsync(secret.ReferenceId, It.IsAny<CancellationToken>()))
              .ReturnsAsync(secret);
        tokens.Setup(t => t.ListBySessionAsync("sid-1", It.IsAny<CancellationToken>()))
              .Returns(Enumerate(new SchemataToken {
                  Type = TokenTypes.AccessToken, Status = TokenStatuses.Valid, SessionId = "sid-1",
              }));
        var created = new List<SchemataToken>();
        tokens.Setup(t => t.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()))
              .Callback((SchemataToken token, CancellationToken _) => created.Add(token))
              .ReturnsAsync((SchemataToken token, CancellationToken _) => token);
        var handler = new NativeSsoTokenExchangeHandler<SchemataApplication>(
            tokens.Object, issuer, apps.Object, Options.Create(options), FixedTime());
        var request = new TokenRequest {
            SubjectToken      = idToken,
            SubjectTokenType  = TokenTypeUris.IdToken,
            ActorToken        = secret.ReferenceId,
            ActorTokenType    = TokenTypeUris.DeviceSecret,
            RequestedTokenType = TokenTypeUris.AccessToken,
            Audience          = Issuer,
            Scope             = "openid profile",
        };
        return new(handler, tokens, source, target, request, created);
    }

    private sealed record NativeFixture(
        NativeSsoTokenExchangeHandler<SchemataApplication> Handler,
        Mock<ITokenStore<SchemataToken>> Tokens,
        SchemataApplication Source,
        SchemataApplication Target,
        TokenRequest Request,
        List<SchemataToken> Created
    );

    private static async IAsyncEnumerable<T> Enumerate<T>(params T[] values) {
        foreach (var value in values) {
            yield return value;
        }
        await Task.CompletedTask;
    }
}

public class SessionStateShould
{
    [Fact]
    public void Build_The_Exact_Sha256_Base64Url_Session_State_Without_Spaces() {
        const string client = "client-1";
        const string origin = "https://client.example";
        const string opstate = "opstate-1";
        const string salt = "0011223344556677";
        var digest = SHA256.HashData(Encoding.ASCII.GetBytes(
            "client-1 https://client.example opstate-1 0011223344556677"));
        var expected = $"{Convert.ToBase64String(digest).TrimEnd('=').Replace('+', '-').Replace('/', '_')}.{salt}";
        var subject  = new SessionStateFormulator();

        var first  = subject.Build(client, origin, opstate, salt);
        var second = subject.Build(client, origin, opstate, "8899AABBCCDDEEFF");

        Assert.Equal(expected, first);
        Assert.DoesNotContain(" ", first);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task Publish_Session_State_For_The_Redirect_Origin_And_Write_A_Public_Opstate_Cookie() {
        var http           = new DefaultHttpContext();
        var sessionOptions = Options.Create(new SessionManagementOptions { OpStateCookieName = "opstate" });
        var sessions = new OpSessionService(
            new HttpContextAccessor { HttpContext = http },
            Options.Create(new SchemataAuthorizationOptions()),
            sessionOptions);
        var advisor = new AdviceAuthorizeSessionState<SchemataApplication>(
            new HttpContextAccessor { HttpContext = http }, sessionOptions, sessions, new SessionStateFormulator());
        using var provider = new ServiceCollection().BuildServiceProvider();
        var ctx = new AdviceContext(provider);
        var authz = new AuthorizeContext<SchemataApplication> {
            Application = new SchemataApplication { ClientId = "client-1" },
            Request     = new AuthorizeRequest { RedirectUri = "https://client.example/callback?x=1" },
        };

        await advisor.AdviseAsync(ctx, authz);

        Assert.True(ctx.TryGet<SessionStateFormulation>(out var foundState));
        var state = Assert.IsType<SessionStateFormulation>(foundState);
        Assert.Equal("https://client.example", state.Origin);
        Assert.Equal(new SessionStateFormulator().Build("client-1", state.Origin, state.OpUaState, state.Salt), state.Value);
        Assert.Equal(ResponseModes.Query, authz.ResponseMode);
        var cookies = http.Response.Headers.SetCookie.Select(value => value?.ToString() ?? string.Empty).ToArray();
        var cookie = Assert.Single(cookies, value => value?.StartsWith("opstate=", StringComparison.Ordinal) == true)!;
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=none", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("httponly", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Skip_Session_State_During_Par_Endpoint_Validation() {
        var http = new DefaultHttpContext();
        var advisor = new AdviceAuthorizeSessionState<SchemataApplication>(
            new HttpContextAccessor { HttpContext = http },
            Options.Create(new SessionManagementOptions()),
            new DefaultOpSessionService(),
            new SessionStateFormulator());
        using var provider = new ServiceCollection().BuildServiceProvider();
        var ctx = new AdviceContext(provider);
        ctx.Set(new ParEndpointValidation());
        var authz = new AuthorizeContext<SchemataApplication> {
            Application = new SchemataApplication { ClientId = "client-1" },
            Request     = new AuthorizeRequest { RedirectUri = "https://client.example/callback" },
        };

        await advisor.AdviseAsync(ctx, authz);

        Assert.False(ctx.TryGet<SessionStateFormulation>(out _));
        Assert.True(string.IsNullOrEmpty(http.Response.Headers.SetCookie));
        Assert.Null(authz.ResponseMode);
    }

    [Fact]
    public async Task Write_Separate_Public_Opstate_And_HttpOnly_Session_Cookies() {
        var http    = new DefaultHttpContext();
        var service = OpSessions(http);

        var sid = await service.IssueAsync(null, "user-1");

        Assert.False(string.IsNullOrWhiteSpace(sid));
        var cookies = http.Response.Headers.SetCookie.Select(value => value?.ToString() ?? string.Empty).ToArray();
        Assert.Equal(2, cookies.Length);
        var opstate = Assert.Single(cookies, value => value?.StartsWith("opstate=", StringComparison.Ordinal) == true)!;
        var sidCookie = Assert.Single(cookies, value => value?.StartsWith("opstate.sid=", StringComparison.Ordinal) == true)!;
        Assert.DoesNotContain(sid!, opstate, StringComparison.Ordinal);
        Assert.DoesNotContain("httponly", opstate, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", opstate, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=none", opstate, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", sidCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"opstate.sid={sid}", sidCookie, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Avoid_SetCookie_Churn_When_The_Session_And_Cookies_Are_Unchanged() {
        var http = new DefaultHttpContext();
        http.Request.Headers.Cookie = "opstate=state-1; opstate.sid=sid-1";
        var service = OpSessions(http);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sid", "sid-1")], "cookie"));

        var sid = await service.IssueAsync(principal, "user-1");

        Assert.Equal("sid-1", sid);
        Assert.True(string.IsNullOrEmpty(http.Response.Headers.SetCookie));
    }

    [Fact]
    public async Task Terminate_Host_Session_Then_Rotate_Opstate_When_Invalidated() {
        var http = new DefaultHttpContext();
        http.Request.Headers.Cookie = "opstate=state-old; opstate.sid=sid-1";
        var terminator = new Mock<IOpSessionTerminator>();
        var service = OpSessions(http, terminator.Object);

        await service.InvalidateAsync(null, "user-1", "sid-1");

        var cookies = http.Response.Headers.SetCookie.Select(value => value?.ToString() ?? string.Empty).ToArray();
        var opstate = Assert.Single(cookies, value => value?.StartsWith("opstate=", StringComparison.Ordinal) == true)!;
        var deletion = Assert.Single(cookies, value => value?.StartsWith("opstate.sid=", StringComparison.Ordinal) == true)!;
        Assert.DoesNotContain("opstate=state-old", opstate, StringComparison.Ordinal);
        Assert.Contains("opstate.sid=", deletion, StringComparison.Ordinal);
        Assert.Contains("expires=", deletion, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", deletion, StringComparison.OrdinalIgnoreCase);
        terminator.Verify(
            value => value.TerminateAsync(null, "user-1", "sid-1", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Delegate_Default_Session_Invalidation_To_The_Host_Terminator() {
        var terminator = new Mock<IOpSessionTerminator>();
        var service = new DefaultOpSessionService(terminator.Object);

        await service.InvalidateAsync(null, "user-1", "sid-1");

        terminator.Verify(value => value.TerminateAsync(null, "user-1", "sid-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Render_A_Client_Scoped_Origin_Check_Using_WebCrypto_Base64Url() {
        var apps = new Mock<IApplicationManager<SchemataApplication>>();
        apps.Setup(a => a.ListAsync(null, It.IsAny<CancellationToken>())).Returns(Enumerate(
            new SchemataApplication {
                ClientId = "native-client",
                RedirectUris = ["https://client.example/callback", "https://client.example/other"],
            },
            new SchemataApplication {
                ClientId = "other-client",
                RedirectUris = ["https://other.example/callback"],
            }));
        var handler = new SessionManagementHandler<SchemataApplication>(
            apps.Object, Options.Create(new SessionManagementOptions { OpStateCookieName = "opstate" }));

        var html = await handler.CheckSessionAsync(CancellationToken.None);

        Assert.Contains("\"native-client\":[\"https://client.example\"]", html, StringComparison.Ordinal);
        Assert.Contains("\"other-client\":[\"https://other.example\"]", html, StringComparison.Ordinal);
        Assert.Contains("crypto.subtle.digest('SHA-256'", html, StringComparison.Ordinal);
        Assert.Contains("btoa(s).replace(/\\+/g,'-').replace(/\\//g,'_').replace(/=+$/,'')", html,
                        StringComparison.Ordinal);
        Assert.Contains("const allowed=ORIGINS[p[0]]", html, StringComparison.Ordinal);
        Assert.Contains("allowed.includes(e.origin)", html, StringComparison.Ordinal);
        Assert.Contains("postMessage(v,e.origin)", html, StringComparison.Ordinal);
        Assert.DoesNotContain("postMessage(v,'*')", html, StringComparison.Ordinal);
        Assert.DoesNotContain("||true", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Carry_Session_State_Into_A_Redirect_Error_Response() {
        var http      = new DefaultHttpContext();
        var action    = new ActionContext(http, new RouteData(), new());
        var exception = new OAuthException(OAuthErrors.InvalidRequest, "invalid request") {
            RedirectUri = "https://client.example/callback",
            ResponseMode = ResponseModes.Query,
        };
        var context = new ExceptionContext(action, new List<IFilterMetadata>()) { Exception = exception };
        SessionStateContext.Set(http, "session-value.salt");
        var filter = new OAuthExceptionFilter(Options.Create(new SchemataAuthorizationOptions()));

        filter.OnException(context);

        var redirect = Assert.IsType<RedirectResult>(context.Result);
        Assert.Contains("session_state=session-value.salt", redirect.Url, StringComparison.Ordinal);
        Assert.Contains("error=invalid_request", redirect.Url, StringComparison.Ordinal);
        Assert.True(context.ExceptionHandled);
    }

    [Fact]
    public async Task Carry_Session_State_Into_A_Successful_Authorization_Callback() {
        var http = new DefaultHttpContext();
        SessionStateContext.Set(http, "session-value.salt");
        using var provider = new ServiceCollection()
                            .AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = http })
                            .BuildServiceProvider();
        var options = new SchemataAuthorizationOptions { Issuer = "https://issuer.example" };
        var tokens  = new Mock<ITokenStore<SchemataToken>>();
        tokens.Setup(t => t.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((SchemataToken token, CancellationToken _) => token);
        var service = new AuthorizationSignInService<SchemataApplication>(
            Options.Create(options),
            Options.Create(new JsonSerializerOptions()),
            TestSecurityKeys.CreateTokenService(options),
            new Mock<IApplicationManager<SchemataApplication>>().Object,
            tokens.Object,
            provider);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new(IdentityClaims.Subject, "user-1")], "grant"));

        var response = await service.IssueAsync(principal, new Dictionary<string, string?> {
            [Properties.ResponseType] = ResponseTypes.Code,
            [Properties.RedirectUri]  = "https://client.example/callback",
            [Properties.ResponseMode] = ResponseModes.Query,
            [Properties.Scope]        = Scopes.OpenId,
        }, AuthorizationSignInResponseKind.Callback);

        Assert.NotNull(response.Callback);
        Assert.Equal("session-value.salt", response.Callback.Parameters[Parameters.SessionState]);
        Assert.False(string.IsNullOrWhiteSpace(response.Callback.Parameters[Parameters.Code]));
    }

    private static OpSessionService OpSessions(DefaultHttpContext http, IOpSessionTerminator? terminator = null) {
        return new(
            new HttpContextAccessor { HttpContext = http },
            Options.Create(new SchemataAuthorizationOptions { SessionIdClaimType = "sid" }),
            Options.Create(new SessionManagementOptions { OpStateCookieName = "opstate" }),
            terminator);
    }

    private static async IAsyncEnumerable<T> Enumerate<T>(params T[] values) {
        foreach (var value in values) {
            yield return value;
        }
        await Task.CompletedTask;
    }
}