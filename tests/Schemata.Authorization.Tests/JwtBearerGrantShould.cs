using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class JwtBearerGrantShould
{
    private const string Issuer   = "https://localhost";
    private const string Identity = "https://jwt-idp.example.com";
    private const string Subject  = "mailto:mike@example.com";

    private static readonly DateTimeOffset Now = new(2026, 8, 26, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Accept_A_Trusted_Assertion_And_Issue_Tokens_For_Its_Subject() {
        using var rsa = RSA.Create(2048);
        var (handler, sp, _) = CreateHandler(
            application: CreateApplication(["s:api:read"]),
            trustedIssuer: (Identity, new RsaSecurityKey(rsa)));
        using var ambient = AdviceContext.Establish(new(sp));

        var request = CreateRequest(Mint(rsa), scope: "api:read");

        var result = await handler.HandleAsync(request, null, CancellationToken.None);

        Assert.Equal(AuthorizationStatus.SignIn, result.Status);
        var principal = result.Principal!;
        Assert.Equal(Subject, principal.FindFirstValue(IdentityClaims.Subject));
        Assert.Equal("test-client", principal.FindFirstValue(Claims.ClientId));
        Assert.Equal(GrantTypes.JwtBearer, result.Properties![Properties.GrantType]);
        Assert.Equal("api:read", result.Properties[Properties.Scope]);
    }

    [Fact]
    public async Task Reject_An_Assertion_From_An_Untrusted_Issuer() {
        using var rsa = RSA.Create(2048);
        var (handler, sp, _) = CreateHandler(trustedIssuer: (null, null));
        using var ambient = AdviceContext.Establish(new(sp));

        var request = CreateRequest(Mint(rsa));

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.HandleAsync(
                                                             request, null, CancellationToken.None));
        Assert.Equal(OAuthErrors.InvalidGrant, ex.Status);
    }

    [Fact]
    public async Task Reject_An_Assertion_Signed_With_A_Key_Other_Than_The_Trusted_One() {
        using var rsa  = RSA.Create(2048);
        using var evil = RSA.Create(2048);
        var (handler, sp, _) = CreateHandler(trustedIssuer: (Identity, new RsaSecurityKey(rsa)));
        using var ambient = AdviceContext.Establish(new(sp));

        var request = CreateRequest(Mint(evil));

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.HandleAsync(
                                                             request, null, CancellationToken.None));
        Assert.Equal(OAuthErrors.InvalidGrant, ex.Status);
    }

    [Fact]
    public async Task Not_Poison_The_Replay_Slots_When_A_Forged_Assertion_Is_Rejected() {
        using var rsa  = RSA.Create(2048);
        using var evil = RSA.Create(2048);
        var burned = false;
        var slots  = new Mock<ITokenStore<SchemataToken>>();
        slots.Setup(
                value => value.GetOrCreateAsync(
                    It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                    It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((string? parent, string provider, string name, string? marker, TimeSpan _, CancellationToken _) => {
                    if (burned) {
                        return new() { Value = "pre-existing" };
                    }

                    burned = true;
                    return new() { Parent = parent, Provider = provider, Name = name, Value = marker };
                });
        var (handler, sp, _) = CreateHandler(
            application: CreateApplication(["s:api:read"]),
            trustedIssuer: (Identity, new RsaSecurityKey(rsa)),
            slots: slots);
        using var ambient = AdviceContext.Establish(new(sp));

        var jti     = Guid.NewGuid().ToString("n");
        var forged  = Mint(evil, jti: jti);
        var trusted = Mint(rsa, jti: jti);

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => handler.HandleAsync(CreateRequest(forged), null, CancellationToken.None));
        Assert.Equal(OAuthErrors.InvalidGrant, ex.Status);

        var result = await handler.HandleAsync(CreateRequest(trusted), null, CancellationToken.None);

        Assert.Equal(AuthorizationStatus.SignIn, result.Status);
    }

    [Fact]
    public async Task Reject_A_Missing_Assertion() {
        var (handler, sp, _) = CreateHandler();
        using var ambient = AdviceContext.Establish(new(sp));

        var request = CreateRequest(assertion: null);

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.HandleAsync(
                                                             request, null, CancellationToken.None));
        Assert.Equal(OAuthErrors.InvalidRequest, ex.Status);
    }

    [Fact]
    public async Task Reject_A_Replayed_Assertion() {
        using var rsa = RSA.Create(2048);
        var slots = new Mock<ITokenStore<SchemataToken>>();
        slots.Setup(
                value => value.GetOrCreateAsync(
                    It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                    It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(new SchemataToken { Value = "pre-existing" });
        var (handler, sp, _) = CreateHandler(
            application: CreateApplication(["s:api:read"]),
            trustedIssuer: (Identity, new RsaSecurityKey(rsa)),
            slots: slots);
        using var ambient = AdviceContext.Establish(new(sp));

        var request = CreateRequest(Mint(rsa));

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.HandleAsync(
                                                             request, null, CancellationToken.None));
        Assert.Equal(OAuthErrors.InvalidGrant, ex.Status);
    }

    [Fact]
    public async Task Reject_A_Scope_Beyond_The_Client_Permissions() {
        using var rsa = RSA.Create(2048);
        var (handler, sp, manager) = CreateHandler(
            application: CreateApplication(["s:api:read"]),
            trustedIssuer: (Identity, new RsaSecurityKey(rsa)));
        using var ambient = AdviceContext.Establish(new(sp));

        manager.Setup(m => m.HasPermissionAsync(
                          It.IsAny<SchemataApplication>(),
                          PermissionPrefixes.Scope + "api:write",
                          It.IsAny<CancellationToken>()))
               .ReturnsAsync(false);

        var request = CreateRequest(Mint(rsa), scope: "api:write");

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.HandleAsync(
                                                             request, null, CancellationToken.None));
        Assert.Equal(OAuthErrors.InvalidScope, ex.Status);
    }

    [Fact]
    public async Task Reject_A_Client_Without_The_Grant_Permission() {
        using var rsa = RSA.Create(2048);
        var (handler, sp, manager) = CreateHandler(
            application: CreateApplication(["s:api:read"]),
            trustedIssuer: (Identity, new RsaSecurityKey(rsa)));
        using var ambient = AdviceContext.Establish(new(sp));

        manager.Setup(m => m.HasPermissionAsync(
                          It.IsAny<SchemataApplication>(),
                          PermissionPrefixes.GrantType + GrantTypes.JwtBearer,
                          It.IsAny<CancellationToken>()))
               .ReturnsAsync(false);

        var request = CreateRequest(Mint(rsa));

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.HandleAsync(
                                                             request, null, CancellationToken.None));
        Assert.Equal(OAuthErrors.UnauthorizedClient, ex.Status);
    }

    private static SchemataApplication CreateApplication(IReadOnlyList<string> permissions) {
        return new() {
            Uid         = Guid.NewGuid(),
            ClientId    = "test-client",
            ClientType  = "confidential",
            Permissions = ["e:/Connect/Token", PermissionPrefixes.GrantType + GrantTypes.JwtBearer, .. permissions],
        };
    }

    private static (JwtBearerGrantHandler<SchemataApplication> handler,
        IServiceProvider sp,
        Mock<IApplicationManager<SchemataApplication>> manager) CreateHandler(
            SchemataApplication?                   application   = null,
            (string? Issuer, SecurityKey? Key)     trustedIssuer = default,
            Mock<ITokenStore<SchemataToken>>?      slots         = null
        ) {
        application ??= CreateApplication(["s:api:read"]);

        var clientAuth = new Mock<IClientAuthenticationService<SchemataApplication>>(MockBehavior.Strict);
        clientAuth.Setup(c => c.AuthenticateAsync(It.IsAny<Dictionary<string, List<string?>>?>(),
                                                  It.IsAny<Dictionary<string, List<string?>>?>(),
                                                  It.IsAny<Dictionary<string, List<string?>>?>(),
                                                  It.IsAny<CancellationToken>()))
                  .ReturnsAsync(application);

        var manager = new Mock<IApplicationManager<SchemataApplication>>(MockBehavior.Strict);
        foreach (var perm in application.Permissions!) {
            manager.Setup(m => m.HasPermissionAsync(application, perm, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(true);
        }

        if (slots is null) {
            slots = new();
            slots.Setup(
                     value => value.GetOrCreateAsync(
                         It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                         It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string? parent, string provider, string name, string? marker, TimeSpan _, CancellationToken _) =>
                    new() { Parent = parent, Provider = provider, Name = name, Value = marker });
        }

        var options = new SchemataAuthorizationOptions { Issuer = Issuer };
        if (trustedIssuer.Issuer is not null && trustedIssuer.Key is not null) {
            options.JwtBearerTrustedIssuers[trustedIssuer.Issuer] = trustedIssuer.Key;
        }

        var services = new ServiceCollection();
        services.AddSingleton(manager.Object);
        services.TryAddEnumerable(ServiceDescriptor
                                     .Scoped<ITokenRequestAdvisor<SchemataApplication>,
                                          AdviceRequestGrantPermission<SchemataApplication>>());
        services.TryAddEnumerable(ServiceDescriptor
                                     .Scoped<ITokenRequestAdvisor<SchemataApplication>,
                                          AdviceRequestScopeValidation<SchemataApplication>>());
        var sp = services.BuildServiceProvider();

        var handler = new JwtBearerGrantHandler<SchemataApplication>(
            clientAuth.Object,
            new(slots.Object, new FakeTimeProvider(Now)),
            new ClientAssertionChannel(),
            Options.Create(options));
        return (handler, sp, manager);
    }

    private static TokenRequest CreateRequest(string? assertion, string? scope = null) {
        return new() {
            ClientId  = "test-client",
            GrantType = GrantTypes.JwtBearer,
            Assertion = assertion,
            Scope     = scope,
        };
    }

    private static string Mint(RSA key, string? jti = null) {
        var descriptor = new SecurityTokenDescriptor {
            Issuer = Identity,
            Claims = new Dictionary<string, object> {
                ["sub"] = Subject,
                ["aud"] = new[] { Issuer },
                ["jti"] = jti ?? Guid.NewGuid().ToString("n"),
            },
            Expires            = Now.AddMinutes(5).UtcDateTime,
            NotBefore          = Now.AddMinutes(-1).UtcDateTime,
            IssuedAt           = Now.AddMinutes(-1).UtcDateTime,
            SigningCredentials = new(new RsaSecurityKey(key), SecurityAlgorithms.RsaSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
