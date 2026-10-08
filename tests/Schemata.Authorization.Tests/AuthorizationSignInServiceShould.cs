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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Microsoft.IdentityModel.JsonWebTokens;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Security.Skeleton.Services;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class AuthorizationSignInServiceShould
{
    [Fact]
    public async Task Issue_Token_Response_Without_Writing_Http_State() {
        using var provider = Provider(out var http);
        var (service, tokens) = Create(provider);
        SchemataToken? created = null;
        tokens.Setup(value => value.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                                               It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
              .Callback((SchemataToken token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => created = token)
              .ReturnsAsync((SchemataToken? token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => token!);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(IdentityClaims.Subject, "user-1"),
        ], "grant"));

        var result = await service.IssueAsync(principal, new Dictionary<string, string?> {
            [Properties.GrantType] = GrantTypes.ClientCredentials,
            [Properties.Scope]     = "api",
        }, AuthorizationSignInResponseKind.Token);

        Assert.NotNull(result.Token);
        Assert.Null(result.Callback);
        Assert.False(string.IsNullOrWhiteSpace(result.Token.AccessToken));
        Assert.Equal(Schemes.Bearer, result.Token.TokenType);
        Assert.Equal("api", result.Token.Scope);
        Assert.NotNull(created);
        Assert.Equal(TokenTypes.AccessToken, created.Type);
        Assert.Null(http.Response.ContentType);
        Assert.Empty(http.Response.Headers);
        Assert.Equal(0, http.Response.Body.Length);
    }

    [Fact]
    public async Task Sequential_Issuance_Does_Not_Inherit_Dpop_Or_Requested_Claims() {
        using var provider = new ServiceCollection()
            .AddSingleton<IClaimsAdvisor, AdviceClaimsDpopBinding>()
            .AddSingleton<IClaimsAdvisor, AdviceClaimsUserinfoRequest>()
            .BuildServiceProvider();
        var (service, tokens) = Create(provider);
        tokens.Setup(t => t.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
            It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
            .ReturnsAsync((SchemataToken token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => token);
        using var ambient = AdviceContext.Establish(new(provider));
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new(IdentityClaims.Subject, "users/u1")], "grant"));
        var first = await service.IssueAsync(principal, new Dictionary<string, string?> {
            [Properties.GrantType] = GrantTypes.ClientCredentials,
            [Properties.DpopJkt] = "thumbprint",
            [Properties.ClaimsRequest] = "{\"userinfo\":{\"email\":null}}",
        }, AuthorizationSignInResponseKind.Token);
        var second = await service.IssueAsync(principal, new Dictionary<string, string?> {
            [Properties.GrantType] = GrantTypes.ClientCredentials,
        }, AuthorizationSignInResponseKind.Token);
        var bound = new JsonWebToken(first.Token!.AccessToken);
        var plain = new JsonWebToken(second.Token!.AccessToken);
        Assert.True(bound.TryGetPayloadValue<JsonElement>(Claims.Cnf, out _));
        Assert.True(bound.TryGetPayloadValue<string>(Claims.UserinfoRequest, out _));
        Assert.False(plain.TryGetPayloadValue<JsonElement>(Claims.Cnf, out _));
        Assert.False(plain.TryGetPayloadValue<string>(Claims.UserinfoRequest, out _));
        Assert.Equal(Schemes.Bearer, second.Token.TokenType);
    }

    [Fact]
    public async Task NonRefresh_Issuance_Does_Not_Replay_Previous_Rotation() {
        using var provider = Provider(out _);
        var (service, tokens) = Create(provider);
        using var ambient = AdviceContext.Establish(new(provider));
        ClaimsPrincipal Principal() => new(new ClaimsIdentity([new(IdentityClaims.Subject, "users/u1")], "grant"));
        await service.IssueAsync(Principal(), new Dictionary<string, string?> {
            [Properties.GrantType] = GrantTypes.RefreshToken,
            [Properties.Scope] = "api",
            [Properties.RefreshPredecessor] = JsonSerializer.Serialize(new SchemataToken { Family = "family", ReferenceId = "old" }, Schemata.Common.SchemataJson.Default),
        }, AuthorizationSignInResponseKind.Token);
        tokens.Setup(t => t.RotateFamilyAsync(It.IsAny<SchemataToken>(), It.IsAny<IReadOnlyCollection<SchemataToken>>(), It.IsAny<CancellationToken>(),
            It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>())).ThrowsAsync(new InvalidOperationException("Old predecessor reused"));
        var independent = await service.IssueAsync(Principal(), new Dictionary<string, string?> {
            [Properties.GrantType] = GrantTypes.ClientCredentials, [Properties.Scope] = "api",
        }, AuthorizationSignInResponseKind.Token);
        Assert.NotNull(independent.Token?.AccessToken);
        Assert.Null(independent.Token!.RefreshToken);
    }

    [Fact]
    public async Task Issue_Authorization_Callback_Without_Rendering_It() {
        using var provider = Provider(out var http);
        var (service, tokens) = Create(provider);
        SchemataToken? created = null;
        tokens.Setup(value => value.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                                               It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
              .Callback((SchemataToken token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => created = token)
              .ReturnsAsync((SchemataToken? token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => token!);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(IdentityClaims.Subject, "user-1"),
        ], "authorize"));

        var result = await service.IssueAsync(principal, new Dictionary<string, string?> {
            [Properties.ResponseType] = ResponseTypes.Code,
            [Properties.RedirectUri]  = "https://client.example/callback",
            [Properties.ResponseMode] = ResponseModes.Query,
            [Properties.State]        = "state-1",
            [Properties.Scope]        = Scopes.OpenId,
        }, AuthorizationSignInResponseKind.Callback);

        Assert.Null(result.Token);
        Assert.NotNull(result.Callback);
        Assert.Equal("https://client.example/callback", result.Callback.RedirectUri);
        Assert.Equal(ResponseModes.Query, result.Callback.ResponseMode);
        Assert.Equal("state-1", result.Callback.Parameters[Parameters.State]);
        Assert.False(string.IsNullOrWhiteSpace(result.Callback.Parameters[Parameters.Code]));
        Assert.NotNull(created);
        Assert.Equal(TokenTypes.AuthorizationCode, created.Type);
        Assert.Null(http.Response.ContentType);
        Assert.Empty(http.Response.Headers);
        Assert.Equal(0, http.Response.Body.Length);
    }

    [Fact]
    public async Task Include_The_Session_In_A_Hybrid_Callback_Id_Token() {
        using var provider = new ServiceCollection()
                            .AddSingleton<IClaimsAdvisor>(new AdviceClaimsAudience(
                                Options.Create(new SchemataAuthorizationOptions {
                                    Issuer = "https://issuer.example",
                                })))
                            .BuildServiceProvider();
        var (service, tokens) = Create(provider);
        tokens.Setup(value => value.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                                               It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
              .ReturnsAsync((SchemataToken? token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => token!);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(IdentityClaims.Subject, "user-1"),
            new(Claims.ClientId, "client-1"),
        ], "authorize"));

        var result = await service.IssueAsync(principal, new Dictionary<string, string?> {
            [Properties.GrantType] = GrantTypes.AuthorizationCode,
            [Properties.ResponseType] = $"{ResponseTypes.Code} {ResponseTypes.IdToken}",
            [Properties.RedirectUri] = "https://client.example/callback",
            [Properties.Scope] = Scopes.OpenId,
            [Properties.SessionId] = "sid-1",
            [Properties.GrantProfile] = GrantProfiles.OpenIdConnect,
        }, AuthorizationSignInResponseKind.Callback);

        var idToken = new JsonWebTokenHandler().ReadJsonWebToken(
            result.Callback!.Parameters[Parameters.IdToken]);
        Assert.Equal("sid-1", idToken.Claims.Single(claim => claim.Type == Claims.SessionId).Value);
    }

    [Fact]
    public async Task Establish_And_Restore_The_Ambient_Context_When_Standalone() {
        var observer = new ObservingClaimsAdvisor();
        using var provider = new ServiceCollection()
                            .AddSingleton<IClaimsAdvisor>(observer)
                            .BuildServiceProvider();
        var (service, tokens) = Create(provider);
        tokens.Setup(value => value.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                                               It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
              .ReturnsAsync((SchemataToken? token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => token!);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(IdentityClaims.Subject, "user-1"),
        ], "grant"));

        Assert.Null(AdviceContext.Current);

        var result = await service.IssueAsync(principal, new Dictionary<string, string?> {
            [Properties.GrantType] = GrantTypes.ClientCredentials,
        }, AuthorizationSignInResponseKind.Token);

        Assert.NotNull(result.Token);
        Assert.NotNull(observer.Context);
        Assert.Same(observer.Context, observer.Ambient);
        Assert.Null(AdviceContext.Current);
    }

    [Fact]
    public async Task Reuse_The_Ambient_Context_When_Already_Established() {
        var observer = new ObservingClaimsAdvisor();
        using var outer = new ServiceCollection()
                         .AddSingleton<IClaimsAdvisor>(observer)
                         .BuildServiceProvider();
        using var inner = new ServiceCollection().BuildServiceProvider();
        var (service, tokens) = Create(inner);
        tokens.Setup(value => value.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                                               It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
              .ReturnsAsync((SchemataToken? token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => token!);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(IdentityClaims.Subject, "user-1"),
        ], "grant"));

        var marker  = new Marker();
        var ambient = new AdviceContext(outer);
        ambient.Set(marker);
        using var scope = AdviceContext.Establish(ambient);

        var result = await service.IssueAsync(principal, new Dictionary<string, string?> {
            [Properties.GrantType] = GrantTypes.ClientCredentials,
        }, AuthorizationSignInResponseKind.Token);

        Assert.NotNull(result.Token);
        Assert.Same(ambient, observer.Context);
        Assert.Same(ambient, observer.Ambient);
        Assert.Same(marker, observer.Marker);
        Assert.Same(ambient, AdviceContext.Current);
    }

    [Fact]
    public async Task Mint_The_Access_Token_Audience_From_The_Default_Resource() {
        using var provider = new ServiceCollection()
                            .AddSingleton<IClaimsAdvisor>(new AdviceClaimsAudience(
                                Options.Create(new SchemataAuthorizationOptions {
                                    Issuer = "https://issuer.example",
                                })))
                            .BuildServiceProvider();
        var (service, tokens) = Create(provider);
        tokens.Setup(value => value.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                                               It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
              .ReturnsAsync((SchemataToken? token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => token!);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(IdentityClaims.Subject, "user-1"),
        ], "grant"));

        var result = await service.IssueAsync(principal, new Dictionary<string, string?> {
            [Properties.GrantType] = GrantTypes.ClientCredentials,
            [Properties.Scope]     = "api",
        }, AuthorizationSignInResponseKind.Token);

        Assert.NotNull(result.Token);
        Assert.NotNull(result.Token.AccessToken);
        var at = new JsonWebTokenHandler().ReadJsonWebToken(result.Token.AccessToken);

        Assert.Contains("https://issuer.example", at.Audiences);
    }

    [Fact]
    public async Task Mint_The_Id_Token_Audience_From_The_Client_Id() {
        using var provider = new ServiceCollection()
                            .AddSingleton<IClaimsAdvisor>(new AdviceClaimsAudience(
                                Options.Create(new SchemataAuthorizationOptions {
                                    Issuer = "https://issuer.example",
                                })))
                            .BuildServiceProvider();
        var (service, tokens) = Create(provider);
        tokens.Setup(value => value.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                                               It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
              .ReturnsAsync((SchemataToken? token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => token!);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(IdentityClaims.Subject, "user-1"),
            new(Claims.ClientId,        "client-1"),
        ], "authorize"));

        var result = await service.IssueAsync(principal, new Dictionary<string, string?> {
            [Properties.GrantType] = GrantTypes.AuthorizationCode,
            [Properties.Scope]     = $"{Scopes.OpenId} api",
            [Properties.Nonce]     = "nonce-1",
            [Properties.GrantProfile] = GrantProfiles.OpenIdConnect,
        }, AuthorizationSignInResponseKind.Token);

        Assert.NotNull(result.Token);
        Assert.NotNull(result.Token.IdToken);
        var id = new JsonWebTokenHandler().ReadJsonWebToken(result.Token.IdToken);

        Assert.Equal("client-1", id.Audiences.Single());
        Assert.DoesNotContain("https://issuer.example", id.Audiences);
    }

    [Fact]
    public async Task Bind_The_Cnf_Claim_And_The_Dpop_Token_Type_When_A_Binding_Is_Present() {
        using var provider = new ServiceCollection()
                            .AddSingleton<IClaimsAdvisor>(new AdviceClaimsDpopBinding())
                            .BuildServiceProvider();
        var (service, tokens) = Create(provider);
        tokens.Setup(value => value.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                                               It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
              .ReturnsAsync((SchemataToken? token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => token!);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(IdentityClaims.Subject, "user-1"),
        ], "grant"));

        var result = await service.IssueAsync(principal, new Dictionary<string, string?> {
            [Properties.GrantType] = GrantTypes.ClientCredentials,
            [Properties.Scope]     = "api",
            [Properties.DpopJkt]   = "0ZcOCORZNYy-DWpqq30jZyJGHTN0d2HglBV3uiguA4I",
        }, AuthorizationSignInResponseKind.Token);
        Assert.NotNull(result.Token);
        Assert.Equal(Schemes.Dpop, result.Token.TokenType);
        Assert.NotNull(result.Token.AccessToken);
        var at = new JsonWebTokenHandler().ReadJsonWebToken(result.Token.AccessToken);
        Assert.True(at.TryGetPayloadValue<JsonElement>(Claims.Cnf, out var cnf));
        Assert.Equal("0ZcOCORZNYy-DWpqq30jZyJGHTN0d2HglBV3uiguA4I", cnf.GetProperty("jkt").GetString());
    }

    /// <summary>The dpop_jkt example value from RFC 9449 §10 Figure 25.</summary>
    private const string Thumbprint = "NzbLsXh8uDCcd-6MNwXF4W_7noWXFZAfHkxZsRGC9Xs";

    [Fact]
    public async Task Carry_The_Dpop_Commitment_Into_The_Authorization_Code_Payload() {
        using var provider = Provider(out var _);
        var (service, tokens) = Create(provider);
        SchemataToken? created = null;
        tokens.Setup(value => value.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                                               It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
              .Callback((SchemataToken token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => created = token)
              .ReturnsAsync((SchemataToken? token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => token!);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(IdentityClaims.Subject, "user-1"),
        ], "authorize"));

        await service.IssueAsync(principal, new Dictionary<string, string?> {
            [Properties.ResponseType] = ResponseTypes.Code,
            [Properties.RedirectUri]  = "https://client.example/callback",
            [Properties.Scope]        = Scopes.OpenId,
            [Properties.DpopJkt]      = Thumbprint,
        }, AuthorizationSignInResponseKind.Callback);
        Assert.NotNull(created);
        Assert.NotNull(created.Payload);
        var code = JsonSerializer.Deserialize<AuthorizationCodePayload>(created.Payload);
        Assert.NotNull(code);
        Assert.NotNull(code.Request);
        Assert.Equal(Thumbprint, code.Request.DpopJkt);
    }
    [Fact]
    public async Task Inherit_The_Cnf_Binding_Into_The_Refresh_Token() {
        using var provider = new ServiceCollection()
                            .AddSingleton<IClaimsAdvisor>(new AdviceClaimsDpopBinding())
                            .BuildServiceProvider();
        var (service, tokens) = Create(provider, TokenFormats.Jwt);
        tokens.Setup(value => value.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                                               It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
              .ReturnsAsync((SchemataToken? token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => token!);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(IdentityClaims.Subject, "user-1"),
        ], "grant"));

        var result = await service.IssueAsync(principal, new Dictionary<string, string?> {
            [Properties.GrantType] = GrantTypes.AuthorizationCode,
            [Properties.Scope]     = $"{Scopes.OpenId} {Scopes.OfflineAccess}",
            [Properties.DpopJkt]   = Thumbprint,
        }, AuthorizationSignInResponseKind.Token);
        Assert.NotNull(result.Token);
        Assert.Equal(Schemes.Dpop, result.Token.TokenType);
        Assert.NotNull(result.Token.RefreshToken);
        var refresh = new JsonWebTokenHandler().ReadJsonWebToken(result.Token.RefreshToken);
        Assert.True(refresh.TryGetPayloadValue<JsonElement>(Claims.Cnf, out var cnf));
        Assert.Equal(Thumbprint, cnf.GetProperty(Claims.Jkt).GetString());
    }

    [Fact]
    public async Task Resolve_One_Context_After_The_Session_And_Persist_The_Same_Event() {
        var expected = new AuthenticationContext("urn:example:acr:step-up", ["pwd", "otp"], 1767225600);
        ClaimsPrincipal? providerPrincipal = null;
        var contexts = new Mock<IAuthenticationContextProvider>();
        contexts.Setup(value => value.GetContextAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
                .Callback<ClaimsPrincipal, CancellationToken>((value, _) => providerPrincipal = value)
                .ReturnsAsync(expected);
        var sessions = new Mock<IOpSessionService>();
        sessions.Setup(value => value.IssueAsync(It.IsAny<ClaimsPrincipal>(), "users/u-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync("sid-resolved");
        using var provider = new ServiceCollection()
                            .AddSingleton(contexts.Object)
                            .AddSingleton<IClaimsAdvisor, AdviceClaimsAuthenticationContext>()
                            .BuildServiceProvider();
        var (service, tokens) = Create(provider, sessions: sessions.Object);
        var created = new List<SchemataToken>();
        tokens.Setup(value => value.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                                               It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
              .Callback((SchemataToken token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => created.Add(token))
              .ReturnsAsync((SchemataToken? token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => token!);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(IdentityClaims.Subject, "users/u-1"),
            new(Claims.ClientId, "client-1"),
        ], "host"));

        var result = await service.IssueAsync(principal, new Dictionary<string, string?> {
            [Properties.GrantType]    = GrantTypes.AuthorizationCode,
            [Properties.ResponseType] = ResponseTypes.Code,
            [Properties.RedirectUri] = "https://client.example/callback",
            [Properties.Scope]       = $"{Scopes.OpenId} {Scopes.OfflineAccess}",
        }, AuthorizationSignInResponseKind.Callback);

        Assert.NotNull(result.Callback);
        Assert.Equal("sid-resolved", providerPrincipal!.FindFirstValue(Claims.SessionId));
        contexts.Verify(value => value.GetContextAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()), Times.Once);
        var row = Assert.Single(created, token => token.Type == TokenTypes.AuthorizationCode);
        Assert.Equal("sid-resolved", row.SessionId);
        var grant = AuthorizationGrantContexts.Deserialize(row.GrantContext);
        Assert.Equal(expected.Acr, grant!.Authentication?.Acr);
        Assert.Equal(expected.Amr, grant.Authentication?.Amr);
        Assert.Equal(expected.AuthTime, grant.Authentication?.AuthTime);
        Assert.Equal("sid-resolved", grant.SessionId);
        Assert.Equal("users/u-1", grant.Subject);
    }

    [Fact]
    public async Task Continuation_Inherits_Stored_Context_Without_Reinvoking_The_Provider() {
        var expected = new AuthenticationContext("urn:example:acr:original", ["pwd"], 1700000000);
        var contexts = new Mock<IAuthenticationContextProvider>(MockBehavior.Strict);
        using var provider = new ServiceCollection()
                            .AddSingleton(contexts.Object)
                            .AddSingleton<IClaimsAdvisor, AdviceClaimsAuthenticationContext>()
                            .BuildServiceProvider();
        var (service, tokens) = Create(provider, TokenFormats.Jwt);
        SchemataToken? created = null;
        tokens.Setup(value => value.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                                               It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
              .Callback((SchemataToken token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => created = token)
              .ReturnsAsync((SchemataToken? token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => token!);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(IdentityClaims.Subject, "users/u-1"),
            new(Claims.ClientId, "client-1"),
            new(Claims.Acr, "drifted"),
        ], "grant"));
        var inherited = AuthorizationGrantContexts.Create(
            "users/u-1", $"{Scopes.OpenId} {Scopes.OfflineAccess}", "sid-original",
            GrantTypes.AuthorizationCode, expected);

        var result = await service.IssueAsync(principal, new Dictionary<string, string?> {
            [Properties.GrantType]    = GrantTypes.RefreshToken,
            [Properties.Scope]       = $"{Scopes.OpenId} {Scopes.OfflineAccess}",
            [Properties.SessionId]   = "sid-original",
            [Properties.GrantContext] = AuthorizationGrantContexts.Serialize(inherited),
        }, AuthorizationSignInResponseKind.Token);

        contexts.VerifyNoOtherCalls();
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(result.Token!.AccessToken);
        Assert.Equal(expected.Acr, jwt.GetClaim(Claims.Acr).Value);
        Assert.Equal("1700000000", jwt.GetClaim(Claims.AuthTime).Value);
        Assert.NotNull(created);
        var persisted = AuthorizationGrantContexts.Deserialize(created.GrantContext)!.Authentication;
        Assert.Equal(expected.Acr, persisted?.Acr);
        Assert.Equal(expected.Amr, persisted?.Amr);
        Assert.Equal(expected.AuthTime, persisted?.AuthTime);
    }

    [Fact]
    public async Task Resolve_Independent_Events_For_Separate_Issuance_Calls_In_One_Pipeline() {
        var contexts = new Mock<IAuthenticationContextProvider>();
        contexts.SetupSequence(value => value.GetContextAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AuthenticationContext("first", ["pwd"], 1700000000))
                .ReturnsAsync(new AuthenticationContext(null, [], null));
        using var provider = new ServiceCollection()
                            .AddSingleton(contexts.Object)
                            .AddSingleton<IClaimsAdvisor, AdviceClaimsAuthenticationContext>()
                            .BuildServiceProvider();
        var (service, tokens) = Create(provider, TokenFormats.Jwt);
        tokens.Setup(value => value.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                                               It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
              .ReturnsAsync((SchemataToken? token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => token!);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(IdentityClaims.Subject, "applications/client-1"),
            new(Claims.ClientId, "client-1"),
        ], "client"));
        using var ambient = AdviceContext.Establish(new(provider));

        var first = await service.IssueAsync(principal, new Dictionary<string, string?> {
            [Properties.GrantType] = GrantTypes.ClientCredentials,
            [Properties.Scope] = "api",
        }, AuthorizationSignInResponseKind.Token);
        var second = await service.IssueAsync(principal, new Dictionary<string, string?> {
            [Properties.GrantType] = GrantTypes.ClientCredentials,
            [Properties.Scope] = "api",
        }, AuthorizationSignInResponseKind.Token);

        var firstJwt = new JsonWebTokenHandler().ReadJsonWebToken(first.Token!.AccessToken);
        var secondJwt = new JsonWebTokenHandler().ReadJsonWebToken(second.Token!.AccessToken);
        Assert.Equal("first", firstJwt.GetClaim(Claims.Acr).Value);
        Assert.False(secondJwt.TryGetPayloadValue<string>(Claims.Acr, out _));
        contexts.Verify(value => value.GetContextAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Persist_The_Same_Absolute_Expiry_In_Refresh_Jwt_Row_And_Grant() {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var clock = new FakeTimeProvider(new(2026, 9, 22, 0, 0, 0, TimeSpan.Zero));
        var (service, tokens) = Create(
            provider, TokenFormats.Jwt, time: clock, refreshLifetime: TimeSpan.FromHours(2));
        SchemataToken? refresh = null;
        tokens.Setup(value => value.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                                               It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
              .Callback((SchemataToken token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => {
                  if (token.Type == TokenTypes.RefreshToken) refresh = token;
              })
              .ReturnsAsync((SchemataToken? token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => token!);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(IdentityClaims.Subject, "users/u-1"),
            new(Claims.ClientId, "client-1"),
        ], "grant"));
        var bound = clock.GetUtcNow().AddMinutes(30);
        var grant = AuthorizationGrantContexts.Create(
            "users/u-1", $"{Scopes.OpenId} {Scopes.OfflineAccess}", null,
            GrantTypes.AuthorizationCode, null, GrantProfiles.OpenIdConnect);
        grant.ExpiresAt = bound;

        await service.IssueAsync(principal, new Dictionary<string, string?> {
            [Properties.GrantType] = GrantTypes.RefreshToken,
            [Properties.Scope] = grant.Scope,
            [Properties.GrantContext] = AuthorizationGrantContexts.Serialize(grant),
        }, AuthorizationSignInResponseKind.Token);

        Assert.NotNull(refresh);
        Assert.Equal(bound.UtcDateTime, refresh.ExpireTime);
        Assert.Equal(bound, AuthorizationGrantContexts.Deserialize(refresh.GrantContext)!.ExpiresAt);
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(refresh.ReferenceId);
        Assert.Equal(bound.UtcDateTime, jwt.ValidTo);
    }

    [Fact]
    public async Task Reject_When_Key_Resolution_Crosses_The_Grant_Deadline() {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var start = new DateTimeOffset(2026, 9, 22, 0, 0, 0, TimeSpan.Zero);
        var clock = new StepTimeProvider(start, start.AddMinutes(1));
        var (service, tokens) = Create(provider, TokenFormats.Jwt, time: clock);
        var grant = AuthorizationGrantContexts.Create(
            "users/u-1", Scopes.OfflineAccess, null,
            GrantTypes.AuthorizationCode, null, GrantProfiles.OAuth);
        grant.ExpiresAt = start.AddSeconds(30);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(IdentityClaims.Subject, "users/u-1"),
            new(Claims.ClientId, "client-1"),
        ], "grant"));

        var exception = await Assert.ThrowsAsync<OAuthException>(() => service.IssueAsync(
            principal, new Dictionary<string, string?> {
                [Properties.GrantType] = GrantTypes.RefreshToken,
                [Properties.Scope] = grant.Scope,
                [Properties.GrantContext] = AuthorizationGrantContexts.Serialize(grant),
            }, AuthorizationSignInResponseKind.Token));

        Assert.Equal(OAuthErrors.InvalidGrant, exception.Status);
        tokens.Verify(value => value.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                                       It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()), Times.Never);
    }

    [Fact]
    public async Task Retain_A_Shorter_Policy_Bound_Across_Repeated_Rotations() {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var clock = new FakeTimeProvider(new(2026, 9, 22, 0, 0, 0, TimeSpan.Zero));
        var (service, tokens) = Create(
            provider, TokenFormats.Reference, time: clock, refreshLifetime: TimeSpan.FromMinutes(30));
        var created = new List<SchemataToken>();
        tokens.Setup(value => value.PublishFamilyAsync(
                         It.IsAny<string>(), It.IsAny<IReadOnlyCollection<SchemataToken>>(),
                         It.IsAny<bool>(), It.IsAny<CancellationToken>(),
                         It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
              .Callback((string _, IReadOnlyCollection<SchemataToken> rows, bool _, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => created.AddRange(rows))
              .ReturnsAsync(true);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(IdentityClaims.Subject, "users/u-1"),
            new(Claims.ClientId, "client-1"),
        ], "grant"));
        var grant = AuthorizationGrantContexts.Create(
            "users/u-1", Scopes.OfflineAccess, null,
            GrantTypes.AuthorizationCode, null, GrantProfiles.OAuth);
        grant.ExpiresAt = clock.GetUtcNow().AddHours(2);

        await service.IssueAsync(principal, new Dictionary<string, string?> {
            [Properties.GrantType] = GrantTypes.RefreshToken,
            [Properties.Scope] = grant.Scope,
            [Properties.GrantContext] = AuthorizationGrantContexts.Serialize(grant),
        }, AuthorizationSignInResponseKind.Token);
        var first = Assert.Single(created, token => token.Type == TokenTypes.RefreshToken);
        var firstGrant = AuthorizationGrantContexts.Deserialize(first.GrantContext)!;

        created.Clear();
        clock.Advance(TimeSpan.FromMinutes(5));
        await service.IssueAsync(principal, new Dictionary<string, string?> {
            [Properties.GrantType] = GrantTypes.RefreshToken,
            [Properties.Scope] = firstGrant.Scope,
            [Properties.GrantContext] = first.GrantContext,
        }, AuthorizationSignInResponseKind.Token);
        var second = Assert.Single(created, token => token.Type == TokenTypes.RefreshToken);

        var expected = new DateTimeOffset(2026, 9, 22, 0, 30, 0, TimeSpan.Zero);
        Assert.Equal(expected.UtcDateTime, first.ExpireTime);
        Assert.Equal(expected.UtcDateTime, second.ExpireTime);
        Assert.Equal(expected, AuthorizationGrantContexts.Deserialize(second.GrantContext)!.ExpiresAt);
    }
    [Fact]
    public async Task Sign_The_Id_Token_With_The_Registered_Algorithm_And_Align_Every_Hash() {
        var store = new TestSecurityStore();
        var ec    = TestSecurityKeys.AddSigningRow(
            store, "https://issuer.example", SecurityConstants.Algorithms.P384);
        ec.CreateTime = DateTime.UtcNow - TimeSpan.FromMinutes(1);
        var rsa = TestSecurityKeys.AddSigningRow(store, "https://issuer.example");
        rsa.CreateTime = DateTime.UtcNow;
        using var provider = new ServiceCollection().BuildServiceProvider();
        var application = new SchemataApplication {
            Uid                      = Guid.NewGuid(),
            ClientId                 = "client-1",
            CanonicalName            = "applications/client-1",
            IdTokenSignedResponseAlg = SigningAlgorithms.EcdsaSha384,
        };
        var (service, tokens) = Create(provider, store: store, application: application);
        tokens.Setup(value => value.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                                               It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
              .ReturnsAsync((SchemataToken? token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => token!);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(IdentityClaims.Subject, "user-1"),
            new(Claims.ClientId, "client-1"),
        ], "grant"));

        var result = await service.IssueAsync(principal, new Dictionary<string, string?> {
            [Properties.GrantType]    = GrantTypes.AuthorizationCode,
            [Properties.Scope]        = Scopes.OpenId,
            [Properties.DeviceSecret] = "device-secret-1",
            [Properties.GrantProfile] = GrantProfiles.OpenIdConnect,
        }, AuthorizationSignInResponseKind.Token);

        Assert.NotNull(result.Token);
        Assert.NotNull(result.Token.AccessToken);
        Assert.NotNull(result.Token.IdToken);
        var at = new JsonWebTokenHandler().ReadJsonWebToken(result.Token.AccessToken);
        var id = new JsonWebTokenHandler().ReadJsonWebToken(result.Token.IdToken);

        // The registered alg governs the ID token only; the access token keeps the primary row.
        Assert.Equal(SigningAlgorithms.RsaSha256, at.Alg);
        Assert.Equal(SigningAlgorithms.EcdsaSha384, id.Alg);
        Assert.Equal(ec.Kid, id.Kid);
        Assert.Equal(ComputeSha384Hash(result.Token.AccessToken), id.GetClaim(Claims.AtHash).Value);
        Assert.Equal(ComputeSha384Hash("device-secret-1"), id.GetClaim(Claims.DsHash).Value);
    }

    [Fact]
    public async Task Fail_Issuance_When_The_Registered_Id_Token_Algorithm_Has_No_Serving_Key() {
        var store = new TestSecurityStore();
        TestSecurityKeys.AddSigningRow(store, "https://issuer.example");
        using var provider = new ServiceCollection().BuildServiceProvider();
        var application = new SchemataApplication {
            Uid                      = Guid.NewGuid(),
            ClientId                 = "client-1",
            CanonicalName            = "applications/client-1",
            IdTokenSignedResponseAlg = SigningAlgorithms.EcdsaSha384,
        };
        var (service, _) = Create(provider, store: store, application: application);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(IdentityClaims.Subject, "user-1"),
            new(Claims.ClientId, "client-1"),
        ], "grant"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.IssueAsync(
            principal, new Dictionary<string, string?> {
                [Properties.GrantType]    = GrantTypes.AuthorizationCode,
                [Properties.Scope]        = Scopes.OpenId,
                [Properties.GrantProfile] = GrantProfiles.OpenIdConnect,
            }, AuthorizationSignInResponseKind.Token));
    }

    [Fact]
    public async Task Resolve_The_Signing_Selection_Once_For_The_Whole_Issuance() {
        var store = new TestSecurityStore();
        TestSecurityKeys.AddSigningRow(store, "https://issuer.example");
        using var provider = new ServiceCollection().BuildServiceProvider();
        var application = new SchemataApplication {
            Uid           = Guid.NewGuid(),
            ClientId      = "client-1",
            CanonicalName = "applications/client-1",
        };
        var (service, tokens) = Create(provider, store: store, application: application);
        tokens.Setup(value => value.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                                               It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
              .ReturnsAsync((SchemataToken? token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => token!);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(IdentityClaims.Subject, "user-1"),
            new(Claims.ClientId, "client-1"),
        ], "grant"));
        var queries = store.ListByParentCalls;

        var result = await service.IssueAsync(principal, new Dictionary<string, string?> {
            [Properties.GrantType]    = GrantTypes.AuthorizationCode,
            [Properties.Scope]        = $"{Scopes.OpenId} {Scopes.OfflineAccess}",
            [Properties.GrantProfile] = GrantProfiles.OpenIdConnect,
        }, AuthorizationSignInResponseKind.Token);

        Assert.NotNull(result.Token);
        Assert.False(string.IsNullOrWhiteSpace(result.Token.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(result.Token.RefreshToken));
        Assert.False(string.IsNullOrWhiteSpace(result.Token.IdToken));
        // One resolution serves the access token, refresh token, and ID token of the
        // operation: one signing-row listing plus one encryption-row listing.
        Assert.Equal(2, store.ListByParentCalls - queries);
    }

    [Fact]
    public async Task Issue_An_OAuth_Only_Grant_With_The_Primary_Selection_When_The_Registered_Id_Token_Algorithm_Is_Unserved() {
        var store = new TestSecurityStore();
        TestSecurityKeys.AddSigningRow(store, "https://issuer.example");
        using var provider = new ServiceCollection().BuildServiceProvider();
        var application = new SchemataApplication {
            Uid                      = Guid.NewGuid(),
            ClientId                 = "client-1",
            CanonicalName            = "applications/client-1",
            IdTokenSignedResponseAlg = SigningAlgorithms.EcdsaSha384,
        };
        var (service, tokens) = Create(provider, store: store, application: application);
        tokens.Setup(value => value.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                                               It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
              .ReturnsAsync((SchemataToken? token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => token!);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(IdentityClaims.Subject, "user-1"),
            new(Claims.ClientId, "client-1"),
        ], "grant"));

        // client_credentials never carries an ID token, so the registered id_token alg
        // (unserved by the store) must not gate the access token.
        var result = await service.IssueAsync(principal, new Dictionary<string, string?> {
            [Properties.GrantType] = GrantTypes.ClientCredentials,
            [Properties.Scope]     = "api",
        }, AuthorizationSignInResponseKind.Token);

        Assert.NotNull(result.Token);
        Assert.NotNull(result.Token.AccessToken);
        Assert.Null(result.Token.IdToken);
        var at = new JsonWebTokenHandler().ReadJsonWebToken(result.Token.AccessToken);
        Assert.Equal(SigningAlgorithms.RsaSha256, at.Alg);
    }

    [Fact]
    public async Task Issue_A_Token_Only_Callback_With_The_Primary_Selection_When_The_Registered_Id_Token_Algorithm_Is_Unserved() {
        var store = new TestSecurityStore();
        TestSecurityKeys.AddSigningRow(store, "https://issuer.example");
        using var provider = new ServiceCollection().BuildServiceProvider();
        var application = new SchemataApplication {
            Uid                      = Guid.NewGuid(),
            ClientId                 = "client-1",
            CanonicalName            = "applications/client-1",
            IdTokenSignedResponseAlg = SigningAlgorithms.EcdsaSha384,
        };
        var (service, tokens) = Create(provider, store: store, application: application);
        tokens.Setup(value => value.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                                               It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
              .ReturnsAsync((SchemataToken? token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => token!);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(IdentityClaims.Subject, "user-1"),
            new(Claims.ClientId, "client-1"),
        ], "authorize"));

        // response_type=token mints no ID token even for an OIDC-profiled grant, so the
        // callback's access token signs with the primary selection.
        var result = await service.IssueAsync(principal, new Dictionary<string, string?> {
            [Properties.GrantType]    = GrantTypes.Implicit,
            [Properties.GrantProfile] = GrantProfiles.OpenIdConnect,
            [Properties.ResponseType] = ResponseTypes.Token,
            [Properties.RedirectUri]  = "https://client.example/callback",
        }, AuthorizationSignInResponseKind.Callback);

        Assert.Null(result.Token);
        Assert.NotNull(result.Callback);
        var at = new JsonWebTokenHandler().ReadJsonWebToken(result.Callback.Parameters[Parameters.AccessToken]);
        Assert.Equal(SigningAlgorithms.RsaSha256, at.Alg);
        Assert.False(result.Callback.Parameters.ContainsKey(Parameters.IdToken));
    }


    [Fact]
    public async Task Persist_The_Established_Generation_On_An_Online_Grant() {
        var sessions = new Mock<IOpSessionService>();
        sessions.Setup(value => value.IssueAsync(It.IsAny<ClaimsPrincipal>(), "users/u-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync("sid-1");
        sessions.Setup(value => value.EstablishOnlineAsync("users/u-1", "sid-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync("gen-1");
        using var provider = new ServiceCollection().BuildServiceProvider();
        var (service, tokens) = Create(provider, sessions: sessions.Object);
        var created = new List<SchemataToken>();
        tokens.Setup(value => value.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                                               It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
              .Callback((SchemataToken token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => created.Add(token))
              .ReturnsAsync((SchemataToken? token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => token!);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(IdentityClaims.Subject, "users/u-1"),
            new(Claims.ClientId, "client-1"),
        ], "host"));

        var result = await service.IssueAsync(principal, new Dictionary<string, string?> {
            [Properties.GrantType]    = GrantTypes.AuthorizationCode,
            [Properties.ResponseType] = ResponseTypes.Code,
            [Properties.RedirectUri]  = "https://client.example/callback",
            [Properties.Scope]        = $"{Scopes.OpenId} {Scopes.DeviceSso}",
            [Properties.GrantProfile] = GrantProfiles.OpenIdConnect,
        }, AuthorizationSignInResponseKind.Callback);

        Assert.NotNull(result.Callback);
        var row = Assert.Single(created, token => token.Type == TokenTypes.AuthorizationCode);
        var grant = AuthorizationGrantContexts.Deserialize(row.GrantContext);
        Assert.Equal(NativeSessionKinds.Online, grant!.NativeSessionKind);
        Assert.Equal("gen-1", grant.OnlineSessionAuthority);
    }

    [Fact]
    public async Task Fail_An_Online_Callback_When_Establishment_Yields_No_Generation() {
        var sessions = new Mock<IOpSessionService>();
        sessions.Setup(value => value.IssueAsync(It.IsAny<ClaimsPrincipal>(), "users/u-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync("sid-1");
        sessions.Setup(value => value.EstablishOnlineAsync("users/u-1", "sid-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync((string?)null);
        using var provider = new ServiceCollection().BuildServiceProvider();
        var (service, tokens) = Create(provider, sessions: sessions.Object);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(IdentityClaims.Subject, "users/u-1"),
            new(Claims.ClientId, "client-1"),
        ], "host"));

        var ex = await Assert.ThrowsAsync<OAuthException>(() => service.IssueAsync(principal, new Dictionary<string, string?> {
            [Properties.GrantType]    = GrantTypes.AuthorizationCode,
            [Properties.ResponseType] = ResponseTypes.Code,
            [Properties.RedirectUri]  = "https://client.example/callback",
            [Properties.Scope]        = $"{Scopes.OpenId} {Scopes.DeviceSso}",
            [Properties.GrantProfile] = GrantProfiles.OpenIdConnect,
        }, AuthorizationSignInResponseKind.Callback));

        Assert.Equal(OAuthErrors.InvalidGrant, ex.Status);
        tokens.Verify(value => value.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                                       It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()), Times.Never);
    }

    [Fact]
    public async Task Fail_Online_Issuance_When_The_Persisted_Generation_No_Longer_Matches() {
        var sessions = new Mock<IOpSessionService>();
        sessions.Setup(value => value.ValidateOnlineAsync("users/u-1", "sid-1", "gen-old", It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
        using var provider = new ServiceCollection().BuildServiceProvider();
        var (service, _) = Create(provider, sessions: sessions.Object);
        var inherited = AuthorizationGrantContexts.Create(
            "users/u-1", $"{Scopes.OpenId} {Scopes.DeviceSso}", "sid-1", GrantTypes.AuthorizationCode, null);
        inherited.OnlineSessionAuthority = "gen-old";
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(IdentityClaims.Subject, "users/u-1"),
            new(Claims.ClientId, "client-1"),
        ], "grant"));

        var ex = await Assert.ThrowsAsync<OAuthException>(() => service.IssueAsync(principal, new Dictionary<string, string?> {
            [Properties.GrantType]    = GrantTypes.RefreshToken,
            [Properties.SessionId]    = "sid-1",
            [Properties.GrantContext] = AuthorizationGrantContexts.Serialize(inherited),
        }, AuthorizationSignInResponseKind.Token));

        Assert.Equal(OAuthErrors.InvalidGrant, ex.Status);
        sessions.Verify(value => value.EstablishOnlineAsync(
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Issue_Online_Tokens_While_The_Persisted_Generation_Matches() {
        var sessions = new Mock<IOpSessionService>();
        sessions.Setup(value => value.ValidateOnlineAsync("users/u-1", "sid-1", "gen-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
        using var provider = new ServiceCollection().BuildServiceProvider();
        var (service, tokens) = Create(provider, sessions: sessions.Object);
        var created = new List<SchemataToken>();
        tokens.Setup(value => value.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                                               It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
              .Callback((SchemataToken token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => created.Add(token))
              .ReturnsAsync((SchemataToken? token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => token!);
        var inherited = AuthorizationGrantContexts.Create(
            "users/u-1", $"{Scopes.OpenId} {Scopes.DeviceSso}", "sid-1", GrantTypes.AuthorizationCode, null);
        inherited.OnlineSessionAuthority = "gen-1";
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(IdentityClaims.Subject, "users/u-1"),
            new(Claims.ClientId, "client-1"),
        ], "grant"));

        var result = await service.IssueAsync(principal, new Dictionary<string, string?> {
            [Properties.GrantType]    = GrantTypes.RefreshToken,
            [Properties.SessionId]    = "sid-1",
            [Properties.GrantContext] = AuthorizationGrantContexts.Serialize(inherited),
        }, AuthorizationSignInResponseKind.Token);

        Assert.NotNull(result.Token);
        sessions.Verify(value => value.EstablishOnlineAsync(
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        var row = Assert.Single(created, token => token.Type == TokenTypes.AccessToken);
        var persisted = AuthorizationGrantContexts.Deserialize(row.GrantContext);
        Assert.Equal("gen-1", persisted!.OnlineSessionAuthority);
    }

    [Fact]
    public async Task Register_The_Participation_Fact_Once_An_Oidc_Token_Is_Published() {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var application = new SchemataApplication {
            Uid = Guid.NewGuid(), ClientId = "client-1", CanonicalName = "applications/client-1",
        };
        var (service, tokens) = Create(provider, application: application);
        var persisted = false;
        tokens.Setup(value => value.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                                               It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
              .Callback((SchemataToken _, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => persisted = true)
              .ReturnsAsync((SchemataToken? token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => token!);
        var registeredAfterPublication = false;
        tokens.Setup(value => value.RegisterParticipantAsync(
                         "users/u-1", "sid-1", "applications/client-1", It.IsAny<CancellationToken>(),
                         It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
              .Callback((string _, string _, string _, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => registeredAfterPublication = persisted)
              .Returns(Task.CompletedTask);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(IdentityClaims.Subject, "users/u-1"),
            new(Claims.ClientId,        "client-1"),
        ], "grant"));

        var result = await service.IssueAsync(principal, new Dictionary<string, string?> {
            [Properties.GrantType]    = GrantTypes.AuthorizationCode,
            [Properties.Scope]        = Scopes.OpenId,
            [Properties.SessionId]    = "sid-1",
            [Properties.GrantProfile] = GrantProfiles.OpenIdConnect,
        }, AuthorizationSignInResponseKind.Token);

        Assert.NotNull(result.Token);
        tokens.Verify(value => value.RegisterParticipantAsync(
            "users/u-1", "sid-1", "applications/client-1", It.IsAny<CancellationToken>(),
            It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()), Times.Once);
        Assert.True(registeredAfterPublication);
    }

    [Fact]
    public async Task Never_Register_Participation_For_An_Oauth_Grant() {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var application = new SchemataApplication {
            Uid = Guid.NewGuid(), ClientId = "client-1", CanonicalName = "applications/client-1",
        };
        var (service, tokens) = Create(provider, application: application);
        tokens.Setup(value => value.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                                               It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
              .ReturnsAsync((SchemataToken? token, CancellationToken _, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => token!);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(IdentityClaims.Subject, "users/u-1"),
            new(Claims.ClientId,        "client-1"),
        ], "grant"));

        var result = await service.IssueAsync(principal, new Dictionary<string, string?> {
            [Properties.GrantType]    = GrantTypes.AuthorizationCode,
            [Properties.Scope]        = "api",
            [Properties.SessionId]    = "sid-1",
            [Properties.GrantProfile] = GrantProfiles.OAuth,
        }, AuthorizationSignInResponseKind.Token);

        Assert.NotNull(result.Token);
        tokens.Verify(value => value.RegisterParticipantAsync(
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()), Times.Never);
    }

    [Fact]
    public async Task Never_Register_Participation_When_Issuance_Fails() {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var start = new DateTimeOffset(2026, 9, 22, 0, 0, 0, TimeSpan.Zero);
        var clock = new StepTimeProvider(start, start.AddMinutes(1));
        var application = new SchemataApplication {
            Uid = Guid.NewGuid(), ClientId = "client-1", CanonicalName = "applications/client-1",
        };
        var (service, tokens) = Create(provider, time: clock, application: application);
        var grant = AuthorizationGrantContexts.Create(
            "users/u-1", Scopes.OpenId, "sid-1",
            GrantTypes.AuthorizationCode, null, GrantProfiles.OpenIdConnect);
        grant.ExpiresAt = start.AddSeconds(30);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(IdentityClaims.Subject, "users/u-1"),
            new(Claims.ClientId,        "client-1"),
        ], "grant"));

        var exception = await Assert.ThrowsAsync<OAuthException>(() => service.IssueAsync(
            principal, new Dictionary<string, string?> {
                [Properties.GrantType]    = GrantTypes.RefreshToken,
                [Properties.Scope]        = grant.Scope,
                [Properties.SessionId]    = "sid-1",
                [Properties.GrantContext] = AuthorizationGrantContexts.Serialize(grant),
            }, AuthorizationSignInResponseKind.Token));

        Assert.Equal(OAuthErrors.InvalidGrant, exception.Status);
        tokens.Verify(value => value.RegisterParticipantAsync(
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()), Times.Never);
    }

    private sealed class StepTimeProvider(DateTimeOffset first, DateTimeOffset later) : TimeProvider
    {
        private int _calls;

        public override DateTimeOffset GetUtcNow() {
            return Interlocked.Increment(ref _calls) == 1 ? first : later;
        }
    }

    private sealed class ObservingClaimsAdvisor : IClaimsAdvisor
    {
        public int Order => 0;

        public AdviceContext? Context { get; private set; }

        public AdviceContext? Ambient { get; private set; }

        public Marker? Marker { get; private set; }

        public Schemata.Authorization.Skeleton.Models.AuthorizationClaimContext? Issuance { get; private set; }

        public Task<AdviseResult> AdviseAsync(AdviceContext ctx, List<Claim> claims, Schemata.Authorization.Skeleton.Models.AuthorizationClaimContext issuance, CancellationToken ct = default) {
            Context = ctx;
            Ambient = AdviceContext.Current;
            Issuance = issuance;
            ctx.TryGet<Marker>(out var marker);
            Marker = marker;
            return Task.FromResult(AdviseResult.Continue);
        }
    }

    private sealed record Marker;

    private static ServiceProvider Provider(out DefaultHttpContext context) {
        context = new();
        return new ServiceCollection()
              .AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = context })
              .BuildServiceProvider();
    }

    private static string ComputeSha384Hash(string value) {
        var digest = SHA384.HashData(Encoding.ASCII.GetBytes(value));
        return Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Encode(
            digest.AsSpan(0, digest.Length / 2).ToArray());
    }

    private static (
        AuthorizationSignInService<SchemataApplication> Service,
        Mock<ITokenStore<SchemataToken>> Tokens
    ) Create(
        IServiceProvider provider,
        string? refreshTokenFormat = null,
        IOpSessionService? sessions = null,
        TimeProvider? time = null,
        TimeSpan? refreshLifetime = null,
        TestSecurityStore? store = null,
        SchemataApplication? application = null
    ) {
        time ??= new FakeTimeProvider(new(2026, 9, 22, 0, 0, 0, TimeSpan.Zero));
        var options = new SchemataAuthorizationOptions {
            Issuer            = "https://issuer.example",
            AccessTokenFormat = TokenFormats.Jwt,
        };
        options.RefreshTokenLifetime = refreshLifetime ?? options.RefreshTokenLifetime;
        if (refreshTokenFormat is not null) {
            options.RefreshTokenFormat = refreshTokenFormat;
        }
        var tokens = new Mock<ITokenStore<SchemataToken>>();
        tokens.Setup(value => value.PublishFamilyAsync(
                         It.IsAny<string>(), It.IsAny<IReadOnlyCollection<SchemataToken>>(),
                         It.IsAny<bool>(), It.IsAny<CancellationToken>(),
                         It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
              .Returns(async (string _, IReadOnlyCollection<SchemataToken> rows, bool _, CancellationToken ct, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => {
                  foreach (var row in rows) await tokens.Object.CreateAsync(row, ct);
                  return true;
              });
        tokens.Setup(value => value.RotateFamilyAsync(
                         It.IsAny<SchemataToken>(), It.IsAny<IReadOnlyCollection<SchemataToken>>(),
                         It.IsAny<CancellationToken>(),
                         It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
              .Returns(async (SchemataToken _, IReadOnlyCollection<SchemataToken> rows, CancellationToken ct, Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>? _) => {
                  foreach (var row in rows) await tokens.Object.CreateAsync(row, ct);
                  return true;
              });
        var apps = new Mock<IApplicationManager<SchemataApplication>>();
        apps.SetupTypedMetadata();
        application ??= new SchemataApplication { ClientId = "client-1", CanonicalName = "applications/client-1" };
        if (application is not null) {
            apps.Setup(value => value.FindByClientIdAsync(application.ClientId!, It.IsAny<CancellationToken>()))
                .ReturnsAsync(application);
        }
        var service = new AuthorizationSignInService<SchemataApplication>(
            Options.Create(options),
            Options.Create(new JsonSerializerOptions()),
            TestSecurityKeys.CreateTokenService(options, store, time: time, seed: store is null),
            apps.Object,
            tokens.Object,
            provider,
            sessions: sessions,
            time: time);
        return (service, tokens);
    }
}
