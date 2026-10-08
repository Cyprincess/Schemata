using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Advice;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
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
        apps.SetupTypedMetadata();
        var tokens  = NewTokenStore();
        var devices = new Mock<IDeviceIdResolver>();
        devices.Setup(d => d.ResolveAsync(null, null, It.IsAny<CancellationToken>())).ReturnsAsync("device-1");
        SchemataToken? created = null;
        tokens.Setup(t => t.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                                               It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
              .Callback((SchemataToken token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => created = token)
              .ReturnsAsync((SchemataToken token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => token);
        var advisor = new AdviceCodeExchangeDeviceSecret<SchemataApplication>(
            apps.Object, tokens.Object, devices.Object, NativeOptions(), FixedTime());
        using var provider = new ServiceCollection().BuildServiceProvider();
        var ctx = new AdviceContext(provider);
        ctx.Set(DeviceGrant("sid-1"));

        await advisor.AdviseAsync(ctx, CodeExchange(app, $"{Scopes.OpenId} {Scopes.DeviceSso}", "sid-1"));

        Assert.True(ctx.TryGet<DeviceSecretIssuance>(out var issuance));
        var prepared = Assert.IsType<SchemataToken>(issuance!.PreparedToken);
        Assert.Equal(TokenTypes.DeviceSecret, prepared.Type);
        Assert.Equal(TokenStatuses.Valid, prepared.Status);
        Assert.Equal(TokenFormats.Reference, prepared.Format);
        Assert.Equal(app.CanonicalName, prepared.Application);
        Assert.Equal("sid-1", prepared.SessionId);
        Assert.Equal("device-1", prepared.DeviceId);
        Assert.Equal(Now.UtcDateTime.AddDays(14), prepared.ExpireTime);
        Assert.Equal(prepared.ReferenceId, issuance.DeviceSecret);
        Assert.Equal("device-1", issuance.DeviceId);
        Assert.Equal(app.CanonicalName, issuance.SourceClientId);
        Assert.Equal("sid-1", issuance.SessionId);
        tokens.Verify(t => t.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                                       It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()), Times.Never);
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
            new(provider), CodeExchange(app, $"{Scopes.OpenId} {Scopes.DeviceSso}", null)));

        Assert.Equal(OAuthErrors.InvalidGrant, ex.Status);
        tokens.Verify(t => t.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                                       It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()), Times.Never);
    }

    [Fact]
    public async Task Reject_A_Device_Secret_Input_On_An_OAuth_Code_Exchange() {
        var app = NativeApplication("native-client");
        var advisor = new AdviceCodeExchangeDeviceSecret<SchemataApplication>(
            ApplicationManager(), NewTokenStore().Object, new NoDeviceIdResolver(), NativeOptions(), FixedTime());
        using var provider = new ServiceCollection().BuildServiceProvider();
        var ctx = new AdviceContext(provider);
        ctx.Set(AuthorizationGrantContexts.Create(
            "user-1", "profile", "sid-1", GrantTypes.AuthorizationCode, null, GrantProfiles.OAuth));
        var exchange = CodeExchange(app, "profile", "sid-1");
        exchange.Request!.DeviceSecret = "secret-1";

        var exception = await Assert.ThrowsAsync<OAuthException>(() => advisor.AdviseAsync(ctx, exchange));

        Assert.Equal(OAuthErrors.InvalidRequest, exception.Status);
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
        tokens.Verify(t => t.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                                       It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()), Times.Never);
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
            advisor.AdviseAsync(new(provider), CodeExchange(app, Scopes.DeviceSso, "sid-1")));

        Assert.Equal(OAuthErrors.InvalidScope, exception.Status);
    }

    [Fact]
    public async Task Reject_Device_Sso_When_The_Client_Lacks_Permission() {
        var app  = NativeApplication("native-client");
        var apps = new Mock<IApplicationManager<SchemataApplication>>();
        // The client never registered the device_sso scope.
        apps.SetupTypedMetadata();
        apps.Setup(a => a.HasScopeAsync(app, Scopes.DeviceSso, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var tokens = NewTokenStore();
        var advisor = new AdviceCodeExchangeDeviceSecret<SchemataApplication>(
            apps.Object, tokens.Object, new NoDeviceIdResolver(), NativeOptions(), FixedTime());
        using var provider = new ServiceCollection().BuildServiceProvider();

        var exception = await Assert.ThrowsAsync<OAuthException>(() => advisor.AdviseAsync(
            new(provider), CodeExchange(app, $"{Scopes.OpenId} {Scopes.DeviceSso}", "sid-1")));

        Assert.Equal(OAuthErrors.InvalidScope, exception.Status);
        tokens.Verify(t => t.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                                       It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()), Times.Never);
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
        ctx.Set(DeviceGrant("sid-1"));
        var exchange = CodeExchange(app, $"{Scopes.OpenId} {Scopes.DeviceSso}", "sid-1");
        exchange.Request!.DeviceSecret = "secret-1";

        await advisor.AdviseAsync(ctx, exchange);

        Assert.True(ctx.TryGet<DeviceSecretIssuance>(out var issuance));
        Assert.Equal("secret-1", issuance!.DeviceSecret);
        Assert.Equal("device-old", issuance.DeviceId);
        tokens.Verify(t => t.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                                       It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()), Times.Never);
    }

    [Fact]
    public async Task Replace_A_Device_Secret_From_An_Older_Authentication_Event() {
        var app      = NativeApplication("native-client");
        var existing = DeviceSecret("secret-old", app.CanonicalName!, "sid-1", "device-1");
        var tokens   = NewTokenStore();
        tokens.Setup(t => t.FindByReferenceIdAsync("secret-old", It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        SchemataToken? created = null;
        tokens.Setup(t => t.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                                               It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
              .Callback((SchemataToken token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => created = token)
              .ReturnsAsync((SchemataToken token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => token);
        var devices = new Mock<IDeviceIdResolver>();
        devices.Setup(d => d.ResolveAsync(null, null, It.IsAny<CancellationToken>())).ReturnsAsync("device-1");
        var advisor = new AdviceCodeExchangeDeviceSecret<SchemataApplication>(
            ApplicationManager(), tokens.Object, devices.Object, NativeOptions(), FixedTime());
        using var provider = new ServiceCollection().BuildServiceProvider();
        var ctx = new AdviceContext(provider);
        var current = DeviceGrant("sid-1");
        current.Authentication = new("urn:example:acr:step-up", ["pwd", "otp"], 1800000000);
        ctx.Set(current);
        var exchange = CodeExchange(app, $"{Scopes.OpenId} {Scopes.DeviceSso}", "sid-1");
        exchange.Request!.DeviceSecret = "secret-old";

        await advisor.AdviseAsync(ctx, exchange);

        Assert.True(ctx.TryGet<DeviceSecretIssuance>(out var issuance));
        var prepared = Assert.IsType<SchemataToken>(issuance!.PreparedToken);
        Assert.NotEqual("secret-old", prepared.ReferenceId);
        var persisted = AuthorizationGrantContexts.Deserialize(prepared.GrantContext);
        Assert.Equal(current.Authentication.Acr, persisted!.Authentication!.Acr);
        Assert.Equal(current.Authentication.AuthTime, persisted.Authentication.AuthTime);
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
        tokens.Setup(t => t.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                                               It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
              .Callback((SchemataToken token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => created = token)
              .ReturnsAsync((SchemataToken token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => token);
        var devices = new Mock<IDeviceIdResolver>();
        devices.Setup(d => d.ResolveAsync(null, null, It.IsAny<CancellationToken>())).ReturnsAsync("device-new");
        var advisor = new AdviceCodeExchangeDeviceSecret<SchemataApplication>(
            ApplicationManager(), tokens.Object, devices.Object, NativeOptions(), FixedTime());
        using var provider = new ServiceCollection().BuildServiceProvider();
        var ctx = new AdviceContext(provider);
        ctx.Set(DeviceGrant("sid-1"));
        var exchange = CodeExchange(app, $"{Scopes.OpenId} {Scopes.DeviceSso}", "sid-1");
        exchange.Request!.DeviceSecret = "secret-old";

        await advisor.AdviseAsync(ctx, exchange);

        Assert.True(ctx.TryGet<DeviceSecretIssuance>(out var issuance));
        var prepared = Assert.IsType<SchemataToken>(issuance!.PreparedToken);
        Assert.NotEqual("secret-old", prepared.ReferenceId);
        Assert.Equal(app.CanonicalName, prepared.Application);
        Assert.Equal("sid-1", prepared.SessionId);
        Assert.Equal("device-new", prepared.DeviceId);
        Assert.Equal(prepared.ReferenceId, issuance.DeviceSecret);
    }

    [Theory]
    [InlineData("superseded-generation")]
    [InlineData("offline-secret")]
    [InlineData("blank-authority")]
    public async Task Replace_A_Device_Secret_Whose_Online_Authority_No_Longer_Matches(string reason) {
        var app      = NativeApplication("native-client");
        var existing = DeviceSecret("secret-old", app.CanonicalName!, "sid-1", "device-1");
        if (reason != "offline-secret") {
            var stored = AuthorizationGrantContexts.Deserialize(existing.GrantContext)!;
            stored.NativeSessionKind      = NativeSessionKinds.Online;
            stored.OnlineSessionAuthority = "gen-old";
            existing.GrantContext         = AuthorizationGrantContexts.Serialize(stored);
        }
        var tokens = NewTokenStore();
        tokens.Setup(t => t.FindByReferenceIdAsync("secret-old", It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        var advisor = new AdviceCodeExchangeDeviceSecret<SchemataApplication>(
            ApplicationManager(), tokens.Object, new NoDeviceIdResolver(), NativeOptions(), FixedTime());
        using var provider = new ServiceCollection().BuildServiceProvider();
        var ctx = new AdviceContext(provider);
        var current = DeviceGrant("sid-1");
        current.NativeSessionKind = NativeSessionKinds.Online;
        if (reason != "blank-authority") {
            current.OnlineSessionAuthority = "gen-new";
        }
        ctx.Set(current);
        var exchange = CodeExchange(app, $"{Scopes.OpenId} {Scopes.DeviceSso}", "sid-1");
        exchange.Request!.DeviceSecret = "secret-old";

        await advisor.AdviseAsync(ctx, exchange);

        Assert.True(ctx.TryGet<DeviceSecretIssuance>(out var issuance));
        var prepared = Assert.IsType<SchemataToken>(issuance!.PreparedToken);
        Assert.NotEqual("secret-old", prepared.ReferenceId);
        Assert.Equal(prepared.ReferenceId, issuance.DeviceSecret);
    }

    [Fact]
    public async Task Reuse_A_Device_Secret_From_The_Current_Online_Generation() {
        var app      = NativeApplication("native-client");
        var existing = DeviceSecret("secret-1", app.CanonicalName!, "sid-1", "device-1");
        var stored   = AuthorizationGrantContexts.Deserialize(existing.GrantContext)!;
        stored.NativeSessionKind      = NativeSessionKinds.Online;
        stored.OnlineSessionAuthority = "gen-1";
        existing.GrantContext         = AuthorizationGrantContexts.Serialize(stored);
        var tokens = NewTokenStore();
        tokens.Setup(t => t.FindByReferenceIdAsync("secret-1", It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        var advisor = new AdviceCodeExchangeDeviceSecret<SchemataApplication>(
            ApplicationManager(), tokens.Object, new NoDeviceIdResolver(), NativeOptions(), FixedTime());
        using var provider = new ServiceCollection().BuildServiceProvider();
        var ctx = new AdviceContext(provider);
        var current = DeviceGrant("sid-1");
        current.NativeSessionKind      = NativeSessionKinds.Online;
        current.OnlineSessionAuthority = "gen-1";
        ctx.Set(current);
        var exchange = CodeExchange(app, $"{Scopes.OpenId} {Scopes.DeviceSso}", "sid-1");
        exchange.Request!.DeviceSecret = "secret-1";

        await advisor.AdviseAsync(ctx, exchange);

        Assert.True(ctx.TryGet<DeviceSecretIssuance>(out var issuance));
        Assert.Equal("secret-1", issuance!.DeviceSecret);
        Assert.Null(issuance.PreparedToken);
    }

    [Fact]
    public async Task Replace_A_Device_Secret_From_A_Broader_Grant_Branch() {
        var app      = NativeApplication("native-client");
        var existing = DeviceSecret("secret-wide", app.CanonicalName!, "sid-1", "device-1");
        var wide = AuthorizationGrantContexts.Deserialize(existing.GrantContext)!;
        wide.Scope = $"{Scopes.OpenId} {Scopes.DeviceSso} api.read";
        existing.GrantContext = AuthorizationGrantContexts.Serialize(wide);
        var tokens = NewTokenStore();
        tokens.Setup(t => t.ListBySessionAsync("sid-1", It.IsAny<CancellationToken>()))
              .Returns(Enumerate(existing));
        SchemataToken? created = null;
        tokens.Setup(t => t.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                                               It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
              .Callback((SchemataToken token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => created = token)
              .ReturnsAsync((SchemataToken token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => token);
        var devices = new Mock<IDeviceIdResolver>();
        devices.Setup(d => d.ResolveAsync(null, null, It.IsAny<CancellationToken>())).ReturnsAsync("device-1");
        var advisor = new AdviceRefreshTokenDeviceSecret<SchemataApplication>(
            ApplicationManager(), tokens.Object, devices.Object, NativeOptions(), FixedTime());
        using var provider = new ServiceCollection().BuildServiceProvider();
        var ctx = new AdviceContext(provider);
        ctx.Set(DeviceGrant("sid-1"));

        await advisor.AdviseAsync(ctx, RefreshExchange(app, "sid-1"));

        Assert.True(ctx.TryGet<DeviceSecretIssuance>(out var issuance));
        var prepared = Assert.IsType<SchemataToken>(issuance!.PreparedToken);
        Assert.NotEqual("secret-wide", prepared.ReferenceId);
        var persisted = AuthorizationGrantContexts.Deserialize(prepared.GrantContext);
        Assert.True(ScopeParser.Parse(DeviceGrant("sid-1").Scope).SetEquals(ScopeParser.Parse(persisted!.Scope)));
    }

    [Fact]
    public async Task Reuse_An_Existing_Bound_Device_Secret_During_Refresh() {
        var app      = NativeApplication("native-client");
        var existing = DeviceSecret("secret-1", app.CanonicalName!, "sid-1", "device-1");
        var tokens   = NewTokenStore();
        tokens.Setup(t => t.ListBySessionAsync("sid-1", It.IsAny<CancellationToken>()))
              .Returns(Enumerate(existing));
        var devices = new Mock<IDeviceIdResolver>();
        devices.Setup(d => d.ResolveAsync(null, null, It.IsAny<CancellationToken>())).ReturnsAsync("device-1");
        var advisor = new AdviceRefreshTokenDeviceSecret<SchemataApplication>(
            ApplicationManager(), tokens.Object, devices.Object, NativeOptions(), FixedTime());
        using var provider = new ServiceCollection().BuildServiceProvider();
        var ctx = new AdviceContext(provider);
        ctx.Set(DeviceGrant("sid-1"));

        await advisor.AdviseAsync(ctx, RefreshExchange(app, "sid-1"));

        Assert.True(ctx.TryGet<DeviceSecretIssuance>(out var issuance));
        Assert.Equal("secret-1", issuance!.DeviceSecret);
        tokens.Verify(t => t.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                                       It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()), Times.Never);
    }

    [Fact]
    public async Task Issue_One_Bound_Device_Secret_When_Refresh_Has_None() {
        var app    = NativeApplication("native-client");
        var tokens = NewTokenStore();
        tokens.Setup(t => t.ListBySessionAsync("sid-1", It.IsAny<CancellationToken>()))
              .Returns(Enumerate<SchemataToken>());
        SchemataToken? created = null;
        tokens.Setup(t => t.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                                               It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
              .Callback((SchemataToken token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => created = token)
              .ReturnsAsync((SchemataToken token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => token);
        var devices = new Mock<IDeviceIdResolver>();
        devices.Setup(d => d.ResolveAsync(null, null, It.IsAny<CancellationToken>())).ReturnsAsync("device-1");
        var advisor = new AdviceRefreshTokenDeviceSecret<SchemataApplication>(
            ApplicationManager(), tokens.Object, devices.Object, NativeOptions(), FixedTime());
        using var provider = new ServiceCollection().BuildServiceProvider();
        var ctx = new AdviceContext(provider);
        ctx.Set(DeviceGrant("sid-1"));

        await advisor.AdviseAsync(ctx, RefreshExchange(app, "sid-1"));

        Assert.True(ctx.TryGet<DeviceSecretIssuance>(out var issuance));
        var prepared = Assert.IsType<SchemataToken>(issuance!.PreparedToken);
        Assert.Equal(app.CanonicalName, prepared.Application);
        Assert.Equal("sid-1", prepared.SessionId);
        Assert.Equal(prepared.ReferenceId, issuance.DeviceSecret);
        tokens.Verify(t => t.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                                       It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()), Times.Never);
    }

    [Fact]
    public async Task Return_Device_Secret_Only_With_An_Id_Token_And_Bind_Its_Hash_And_Session() {
        var options = new SchemataAuthorizationOptions { Issuer = Issuer, AccessTokenFormat = TokenFormats.Jwt };
        var issuer  = TestSecurityKeys.CreateTokenService(options, time: FixedTime());
        var app     = NativeApplication("native-client");
        var apps    = new Mock<IApplicationManager<SchemataApplication>>();
        apps.SetupTypedMetadata();
        apps.Setup(a => a.FindByClientIdAsync(app.ClientId, It.IsAny<CancellationToken>())).ReturnsAsync(app);
        var tokens = NewTokenStore();
        tokens.Setup(t => t.PublishFamilyAsync(
                         It.IsAny<string>(), It.IsAny<IReadOnlyCollection<SchemataToken>>(),
                         It.IsAny<bool>(), It.IsAny<CancellationToken>(),
                         It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>())).ReturnsAsync(true);
        tokens.Setup(t => t.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                                               It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
              .ReturnsAsync((SchemataToken token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => token);
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

        var grant = DeviceGrant("sid-1");
        grant.Scope = $"{Scopes.OpenId} {Scopes.DeviceSso}";
        grant.NativeSessionKind = null;

        var issued = await service.IssueAsync(principal, new Dictionary<string, string?> {
            [Properties.GrantType] = GrantTypes.AuthorizationCode,
            [Properties.Scope]     = $"{Scopes.OpenId} {Scopes.DeviceSso}",
            [Properties.SessionId] = "sid-1",
            [Properties.DeviceSecret] = "device-secret-1",
            [Properties.GrantProfile] = GrantProfiles.OpenIdConnect,
            [Properties.GrantContext] = AuthorizationGrantContexts.Serialize(grant),
        }, AuthorizationSignInResponseKind.Token);

        Assert.NotNull(issued.Token);
        Assert.Equal("device-secret-1", issued.Token.DeviceSecret);
        Assert.NotNull(issued.Token.IdToken);
        var idToken = new JsonWebTokenHandler().ReadJsonWebToken(issued.Token.IdToken);
        await using var context = await issuer.BeginSigningAsync();
        Assert.True(idToken.TryGetPayloadValue<string>(Claims.DsHash, out var dsHash));
        Assert.True(idToken.TryGetPayloadValue<string>(Claims.SessionId, out var sessionId));
        Assert.Equal(TokenService.ComputeHash("device-secret-1", context.Signing), dsHash);
        Assert.Equal("sid-1", sessionId);
    }

    [Fact]
    public async Task Include_The_Session_In_An_Id_Token_Without_Issuing_A_Device_Secret() {
        var options = new SchemataAuthorizationOptions { Issuer = Issuer, AccessTokenFormat = TokenFormats.Jwt };
        var issuer = TestSecurityKeys.CreateTokenService(options, time: FixedTime());
        var app = NativeApplication("native-client");
        var apps = new Mock<IApplicationManager<SchemataApplication>>();
        apps.SetupTypedMetadata();
        apps.Setup(a => a.FindByClientIdAsync(app.ClientId, It.IsAny<CancellationToken>())).ReturnsAsync(app);
        var tokens = NewTokenStore();
        tokens.Setup(t => t.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                                               It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
              .ReturnsAsync((SchemataToken token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => token);
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

        var issued = await service.IssueAsync(principal, new Dictionary<string, string?> {
            [Properties.GrantType] = GrantTypes.TokenExchange,
            [Properties.Scope] = Scopes.OpenId,
            [Properties.SessionId] = "sid-1",
            [Properties.GrantProfile] = GrantProfiles.OpenIdConnect,
        }, AuthorizationSignInResponseKind.Token);

        Assert.NotNull(issued.Token?.IdToken);
        var idToken = new JsonWebTokenHandler().ReadJsonWebToken(issued.Token.IdToken);
        Assert.Equal("sid-1", idToken.Claims.Single(claim => claim.Type == Claims.SessionId).Value);
        Assert.Null(issued.Token.DeviceSecret);
        Assert.DoesNotContain(idToken.Claims, claim => claim.Type == Claims.DsHash);
    }

    [Fact]
    public async Task Return_A_Canonical_SignIn_For_A_Valid_Native_Sso_Profile() {
        using var fixture = await NativeExchangeFixture();
        fixture.Request.Resource = ["https://resource.example/api"];

        var result = await fixture.Handler.HandleAsync(fixture.Target, fixture.Request, null, CancellationToken.None);

        Assert.Equal(AuthorizationStatus.SignIn, result.Status);
        Assert.Equal("user-1", result.Principal!.FindFirstValue(IdentityClaims.Subject));
        Assert.Equal(fixture.Target.ClientId, result.Principal!.FindFirstValue(Claims.ClientId));
        Assert.Equal(GrantTypes.TokenExchange, result.Properties![Properties.GrantType]);
        Assert.Equal("openid profile", result.Properties[Properties.Scope]);
        Assert.Equal("sid-1", result.Properties[Properties.SessionId]);
        Assert.Equal("https://resource.example/api", result.Properties[Properties.Resources]);
        Assert.Equal(TokenTypeUris.AccessToken, result.Properties[Properties.IssuedTokenType]);
        Assert.Empty(fixture.Created);
    }

    [Fact]
    public async Task Reject_Native_Sso_When_Target_Registration_Has_Scopes_But_No_Consent() {
        using var fixture = await NativeExchangeFixture(targetConsent: false);
        var error = await Assert.ThrowsAsync<OAuthException>(() => fixture.Handler.HandleAsync(
            fixture.Target, fixture.Request, null, CancellationToken.None));
        Assert.Equal(OAuthErrors.InteractionRequired, error.Status);
        Assert.Empty(fixture.Created);
    }

    [Fact]
    public async Task Accept_Independent_Target_Consent_Beyond_Source_Registration() {
        using var fixture = await NativeExchangeFixture();
        fixture.Source.Scope = "openid device_sso";
        var result = await fixture.Handler.HandleAsync(fixture.Target, fixture.Request, null, CancellationToken.None);
        Assert.Equal(AuthorizationStatus.SignIn, result.Status);
        Assert.Equal("openid profile", result.Properties![Properties.Scope]);
    }

    [Fact]
    public async Task Require_Target_Consent_For_Default_Persisted_Source_Scopes() {
        using var fixture = await NativeExchangeFixture();
        fixture.Request.Scope = null;
        var error = await Assert.ThrowsAsync<OAuthException>(() => fixture.Handler.HandleAsync(
            fixture.Target, fixture.Request, null, CancellationToken.None));
        Assert.Equal(OAuthErrors.InteractionRequired, error.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reject_Native_Sso_Exchange_With_Multiple_Audiences(bool duplicate) {
        using var fixture = await NativeExchangeFixture();
        fixture.Request.Audience = [Issuer, duplicate ? Issuer : "https://other.example"];
        var error = await Assert.ThrowsAsync<OAuthException>(() => fixture.Handler.HandleAsync(
            fixture.Target, fixture.Request, null, CancellationToken.None));
        Assert.Equal(OAuthErrors.InvalidGrant, error.Status);
        fixture.Tokens.Verify(t => t.FindByReferenceIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reject_Native_Sso_Audience_With_A_Case_Insensitive_Collection() {
        using var fixture = await NativeExchangeFixture();
        fixture.Request.Audience = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Issuer.ToUpperInvariant() };
        var error = await Assert.ThrowsAsync<OAuthException>(() => fixture.Handler.HandleAsync(
            fixture.Target, fixture.Request, null, CancellationToken.None));
        Assert.Equal(OAuthErrors.InvalidGrant, error.Status);
    }

    [Fact]
    public async Task Reject_Native_Sso_Exchange_With_An_Unknown_Device_Secret() {
        using var fixture = await NativeExchangeFixture();
        fixture.Tokens.Setup(t => t.FindByReferenceIdAsync("device-secret-1", It.IsAny<CancellationToken>()))
               .ReturnsAsync((SchemataToken?)null);

        var ex = await Assert.ThrowsAsync<OAuthException>(() => fixture.Handler.HandleAsync(
            fixture.Target, fixture.Request, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidGrant, ex.Status);
        Assert.Empty(fixture.Created);
    }

    [Fact]
    public async Task Reject_Native_Sso_Exchange_When_The_Id_Token_Signature_Is_Invalid() {
        using var fixture = await NativeExchangeFixture();
        var foreignIssuer = TestSecurityKeys.CreateTokenService(
            new() { Issuer = Issuer }, time: FixedTime());
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
        using var fixture = await NativeExchangeFixture(dsHash: "wrong-hash");

        var ex = await Assert.ThrowsAsync<OAuthException>(() => fixture.Handler.HandleAsync(
            fixture.Target, fixture.Request, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidGrant, ex.Status);
        Assert.Empty(fixture.Created);
    }

    [Fact]
    public async Task Reject_Native_Sso_Exchange_When_The_Offline_Family_Is_Invalid() {
        using var fixture = await NativeExchangeFixture();
        fixture.Tokens.Setup(t => t.IsFamilyActiveAsync("family-1", It.IsAny<CancellationToken>()))
               .ReturnsAsync(false);

        var exception = await Assert.ThrowsAsync<OAuthException>(() => fixture.Handler.HandleAsync(
            fixture.Target, fixture.Request, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidGrant, exception.Status);
        Assert.Empty(fixture.Created);
    }

    [Fact]
    public async Task Accept_Native_Sso_With_An_Expired_Id_Hint_While_The_Offline_Family_Is_Active() {
        using var fixture = await NativeExchangeFixture(expiredId: true);

        var result = await fixture.Handler.HandleAsync(fixture.Target, fixture.Request, null, CancellationToken.None);

        Assert.Equal(AuthorizationStatus.SignIn, result.Status);
        Assert.Equal("user-1", result.Principal!.FindFirstValue(IdentityClaims.Subject));
    }

    [Theory]
    [InlineData(false, true)]

    [InlineData(true, false)]
    public async Task Reject_Native_Sso_Exchange_When_Either_Client_Lacks_Permission(
        bool sourcePermitted,
        bool targetPermitted
    ) {
        using var fixture = await NativeExchangeFixture(sourcePermitted: sourcePermitted, targetPermitted: targetPermitted);

        var ex = await Assert.ThrowsAsync<OAuthException>(() => fixture.Handler.HandleAsync(
            fixture.Target, fixture.Request, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.UnauthorizedClient, ex.Status);
        Assert.Empty(fixture.Created);
    }


    [Fact]
    public async Task Route_Token_Exchange_To_The_Composite_Registration_Before_The_Subject_Fallback() {
        var target = NativeApplication("target-client");
        var apps   = new Mock<IApplicationManager<SchemataApplication>>();
        apps.SetupTypedMetadata();
        var tokens = NewTokenStore();
        tokens.Setup(t => t.FindByReferenceIdAsync("missing-secret", It.IsAny<CancellationToken>()))
              .ReturnsAsync((SchemataToken?)null);
        using var nativeServices = new ServiceCollection().BuildServiceProvider();
        var native = new NativeSsoTokenExchangeHandler<SchemataApplication, SchemataAuthorization>(
            tokens.Object,
            TestSecurityKeys.CreateTokenService(new() { Issuer = Issuer }, time: FixedTime()),
            apps.Object,
            ServerOptions(),
            new Mock<ISubjectIdentifierService>().Object,
            nativeServices,
            Mock.Of<IAuthorizationManager<SchemataAuthorization>>(), new ExplicitConsentModelProvider(),
            FixedTime());
        var fallback = new Mock<ITokenExchangeHandler<SchemataApplication>>();
        fallback.Setup(f => f.HandleAsync(It.IsAny<SchemataApplication>(), It.IsAny<TokenRequest>(),
                                           It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("fallback selected"));
        var authenticator = new Mock<IClientAuthentication<SchemataApplication>>();
        authenticator.SetupGet(a => a.Method).Returns(ClientAuthMethods.None);
        authenticator.Setup(a => a.AuthenticateAsync(null, It.IsAny<Dictionary<string, List<string?>>?>(), null,
                                                       It.IsAny<CancellationToken>(),
                                                       It.IsAny<string?>()))
                     .ReturnsAsync(target);
        var clientApps = new Mock<IApplicationManager<SchemataApplication>>();
        clientApps.Setup(a => a.FindByClientIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(target);
        var auth = new ClientAuthenticationService<SchemataApplication>([authenticator.Object], new(), clientApps.Object);
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
            Audience          = [Issuer],
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
        var services = new Mock<IServiceProvider>();
        services.Setup(provider => provider.GetService(typeof(IRepository<SchemataToken>))).Returns(repository.Object);
        var store = new RepositoryTokenStore(services.Object, FixedTime());

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
            CanonicalName = $"applications/resource-{clientId}",
            GrantTypes    = [GrantTypes.AuthorizationCode, GrantTypes.RefreshToken, GrantTypes.TokenExchange],
            Scope         = $"{Scopes.DeviceSso} {Scopes.OpenId} profile",
            TokenEndpointAuthMethod = ClientAuthMethods.None,
        };
    }

    private static IApplicationManager<SchemataApplication> ApplicationManager() {
        return new SchemataApplicationManager<SchemataApplication, SchemataAuthorization>(
            new Mock<IServiceProvider>().Object,
            new Mock<IResourceMutation<SchemataApplication>>().Object);
    }

    private static Mock<ITokenStore<SchemataToken>> NewTokenStore() {
        var store = new Mock<ITokenStore<SchemataToken>>(MockBehavior.Loose);
        store.Setup(value => value.IsFamilyActiveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(true);
        return store;
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
            Family      = "family-1",
            GrantContext = AuthorizationGrantContexts.Serialize(new() {
                Subject        = "user-1",
                SubjectKind    = GrantSubjectKinds.EndUser,
                Profile        = GrantProfiles.OpenIdConnect,
                NativeSessionKind = NativeSessionKinds.Offline,
                Source         = GrantTypes.AuthorizationCode,
                Scope          = $"{Scopes.OpenId} {Scopes.DeviceSso}",
                FamilyEstablished = true,
                SessionId      = sid,
                Family         = "family-1",
            }),
            ExpireTime  = Now.UtcDateTime.AddHours(1),
        };
    }

    private static AuthorizationGrantContext DeviceGrant(string sid) {
        var grant = AuthorizationGrantContexts.Create(
            "user-1", $"{Scopes.OpenId} {Scopes.DeviceSso}", sid,
            GrantTypes.AuthorizationCode, null, GrantProfiles.OpenIdConnect);
        grant.Family = "family-1";
        grant.NativeSessionKind = NativeSessionKinds.Offline;
        grant.FamilyEstablished = true;
        return grant;
    }

    private static CodeExchangeContext<SchemataApplication> CodeExchange(
        SchemataApplication app,
        string              scope,
        string?             sid
    ) {
        return new() {
            Application = app,
            Request     = new() { Scope     = scope },
            Payload     = new() { Scope     = scope },
            CodeToken   = new() { SessionId = sid },
        };
    }

    private static RefreshTokenContext<SchemataApplication> RefreshExchange(SchemataApplication app, string sid) {
        return new() {
            Application = app,
            Request     = new() { Scope     = $"{Scopes.OpenId} {Scopes.DeviceSso}" },
            Token       = new() { SessionId = sid },
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
        bool targetPermitted = true,
        bool expiredId = false,
        bool targetConsent = true
    ) {
        var options = new SchemataAuthorizationOptions { Issuer = Issuer };
        var time = new Mock<TimeProvider>();
        time.Setup(t => t.GetUtcNow()).Returns(expiredId ? Now.AddHours(-2) : Now);
        var issuer  = TestSecurityKeys.CreateTokenService(options, time: time.Object);
        var source  = NativeApplication("source-client");
        var target  = NativeApplication("target-client");
        var secret  = DeviceSecret("device-secret-1", source.CanonicalName!, "sid-1", "device-1");
        secret.Family = "family-1";
        var secretGrant = AuthorizationGrantContexts.Deserialize(secret.GrantContext)!;
        secretGrant.Family = secret.Family;
        secretGrant.FamilyEstablished = true;
        secret.GrantContext = AuthorizationGrantContexts.Serialize(secretGrant);
        await using var context = await issuer.BeginSigningAsync();
        var issuedAt = time.Object.GetUtcNow();
        var idToken = issuer.CreateToken(context, context.Signing, [
            new(IdentityClaims.Subject, "user-1"),
            new(Claims.Audience, source.ClientId!),
            new(Claims.DsHash, dsHash ?? TokenService.ComputeHash(secret.ReferenceId!, context.Signing)),
            new(Claims.SessionId, "sid-1"),
        ], issuedAt, issuedAt + TimeSpan.FromHours(1));
        time.Setup(t => t.GetUtcNow()).Returns(Now);
        var apps = new Mock<IApplicationManager<SchemataApplication>>();
        apps.SetupTypedMetadata();
        apps.Setup(a => a.FindByClientIdAsync(source.ClientId, It.IsAny<CancellationToken>())).ReturnsAsync(source);
        apps.Setup(a => a.FindByClientIdAsync(target.ClientId, It.IsAny<CancellationToken>())).ReturnsAsync(target);
        // The permitted toggles act on the typed scope metadata the runtime actually checks.
        apps.Setup(a => a.HasScopeAsync(source, Scopes.DeviceSso, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourcePermitted);
        apps.Setup(a => a.HasScopeAsync(target, Scopes.DeviceSso, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetPermitted);
        var tokens = NewTokenStore();
        tokens.Setup(t => t.FindByReferenceIdAsync(secret.ReferenceId, It.IsAny<CancellationToken>()))
              .ReturnsAsync(secret);
        tokens.Setup(t => t.ListBySessionAsync("sid-1", It.IsAny<CancellationToken>()))
              .Returns(Enumerate(new SchemataToken {
                  Type = TokenTypes.AccessToken, Status = TokenStatuses.Valid, SessionId = "sid-1",
                  Application = source.CanonicalName, Parent = "user-1", ExpireTime = Now.UtcDateTime.AddHours(1),
              }));
        var created = new List<SchemataToken>();
        tokens.Setup(t => t.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                                               It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
              .Callback((SchemataToken token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => created.Add(token))
              .ReturnsAsync((SchemataToken token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => token);
        var subjects = new Mock<ISubjectIdentifierService>();
        subjects.Setup(s => s.Resolve("user-1", source)).Returns("user-1");
        var services = new ServiceCollection().BuildServiceProvider();
        var authorizations = new Mock<IAuthorizationManager<SchemataAuthorization>>();
        authorizations.Setup(a => a.ListAsync("user-1", target.CanonicalName, It.IsAny<CancellationToken>()))
            .Returns(Enumerate(targetConsent ? new[] { new SchemataAuthorization {
                Application = target.CanonicalName, Subject = "user-1", Status = TokenStatuses.Valid,
                Type = AuthorizationTypes.Permanent, Scopes = "openid profile",
            } } : Array.Empty<SchemataAuthorization>()));
        var handler = new NativeSsoTokenExchangeHandler<SchemataApplication, SchemataAuthorization>(
            tokens.Object, issuer, apps.Object, Options.Create(options), subjects.Object, services,
            authorizations.Object, new ExplicitConsentModelProvider(), FixedTime());
        var request = new TokenRequest {
            SubjectToken      = idToken,
            SubjectTokenType  = TokenTypeUris.IdToken,
            ActorToken        = secret.ReferenceId,
            ActorTokenType    = TokenTypeUris.DeviceSecret,
            RequestedTokenType = TokenTypeUris.AccessToken,
            Audience          = [Issuer],
            Scope             = "openid profile",
        };
        return new(handler, tokens, source, target, request, created, services);
    }

    private sealed record NativeFixture(
        NativeSsoTokenExchangeHandler<SchemataApplication, SchemataAuthorization> Handler,
        Mock<ITokenStore<SchemataToken>> Tokens,
        SchemataApplication Source,
        SchemataApplication Target,
        TokenRequest Request,
        List<SchemataToken> Created,
        ServiceProvider Services
    ) : IDisposable
    {
        public void Dispose() => Services.Dispose();
    }

    private static async IAsyncEnumerable<T> Enumerate<T>(params T[] values) {
        foreach (var value in values) {
            yield return value;
        }
        await Task.CompletedTask;
    }
}