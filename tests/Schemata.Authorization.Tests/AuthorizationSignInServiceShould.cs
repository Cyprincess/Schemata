using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Microsoft.IdentityModel.JsonWebTokens;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Security.Skeleton.Services;
using Schemata.Authorization.Skeleton.Models;
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
        tokens.Setup(value => value.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()))
              .Callback((SchemataToken token, CancellationToken _) => created = token)
              .ReturnsAsync((SchemataToken? token, CancellationToken _) => token);
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
    public async Task Issue_Authorization_Callback_Without_Rendering_It() {
        using var provider = Provider(out var http);
        var (service, tokens) = Create(provider);
        SchemataToken? created = null;
        tokens.Setup(value => value.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()))
              .Callback((SchemataToken token, CancellationToken _) => created = token)
              .ReturnsAsync((SchemataToken? token, CancellationToken _) => token);
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
    public async Task Establish_And_Restore_The_Ambient_Context_When_Standalone() {
        var observer = new ObservingClaimsAdvisor();
        using var provider = new ServiceCollection()
                            .AddSingleton<IClaimsAdvisor>(observer)
                            .BuildServiceProvider();
        var (service, tokens) = Create(provider);
        tokens.Setup(value => value.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((SchemataToken? token, CancellationToken _) => token);
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
        tokens.Setup(value => value.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((SchemataToken? token, CancellationToken _) => token);
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
        tokens.Setup(value => value.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((SchemataToken? token, CancellationToken _) => token);
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
        tokens.Setup(value => value.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((SchemataToken? token, CancellationToken _) => token);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(IdentityClaims.Subject, "user-1"),
            new(Claims.ClientId,        "client-1"),
        ], "authorize"));

        var result = await service.IssueAsync(principal, new Dictionary<string, string?> {
            [Properties.GrantType] = GrantTypes.AuthorizationCode,
            [Properties.Scope]     = $"{Scopes.OpenId} api",
            [Properties.Nonce]     = "nonce-1",
        }, AuthorizationSignInResponseKind.Token);

        Assert.NotNull(result.Token);
        Assert.NotNull(result.Token.IdToken);
        var id = new JsonWebTokenHandler().ReadJsonWebToken(result.Token.IdToken);

        Assert.Equal("client-1", id.Audiences.Single());
        Assert.DoesNotContain("https://issuer.example", id.Audiences);
    }

    [Fact]
    public async Task Bind_The_Cnf_Claim_And_The_Dpop_Token_Type_When_A_Binding_Is_Present() {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var (service, tokens) = Create(provider);
        tokens.Setup(value => value.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((SchemataToken? token, CancellationToken _) => token);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(IdentityClaims.Subject, "user-1"),
        ], "grant"));

        var ambient = new AdviceContext(provider);
        ambient.Set(new DpopBinding("0ZcOCORZNYy-DWpqq30jZyJGHTN0d2HglBV3uiguA4I"));
        using var scope = AdviceContext.Establish(ambient);

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
        tokens.Setup(value => value.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()))
              .Callback((SchemataToken token, CancellationToken _) => created = token)
              .ReturnsAsync((SchemataToken? token, CancellationToken _) => token);
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
        using var provider = Provider(out var _);
        var (service, tokens) = Create(provider, TokenFormats.Jwt);
        tokens.Setup(value => value.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((SchemataToken? token, CancellationToken _) => token);
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

    private sealed class ObservingClaimsAdvisor : IClaimsAdvisor
    {
        public int Order => 0;

        public AdviceContext? Context { get; private set; }

        public AdviceContext? Ambient { get; private set; }

        public Marker? Marker { get; private set; }

        public Task<AdviseResult> AdviseAsync(AdviceContext ctx, List<Claim> claims, CancellationToken ct = default) {
            Context = ctx;
            Ambient = AdviceContext.Current;
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

    private static (
        AuthorizationSignInService<SchemataApplication> Service,
        Mock<ITokenStore<SchemataToken>> Tokens
    ) Create(IServiceProvider provider, string? refreshTokenFormat = null) {
        var options = new SchemataAuthorizationOptions {
            Issuer            = "https://issuer.example",
            AccessTokenFormat = TokenFormats.Jwt,
        };
        if (refreshTokenFormat is not null) {
            options.RefreshTokenFormat = refreshTokenFormat;
        }
        var tokens = new Mock<ITokenStore<SchemataToken>>();
        var apps   = new Mock<IApplicationManager<SchemataApplication>>();
        var service = new AuthorizationSignInService<SchemataApplication>(
            Options.Create(options),
            Options.Create(new JsonSerializerOptions()),
            TestSecurityKeys.CreateTokenService(options),
            apps.Object,
            tokens.Object,
            provider);
        return (service, tokens);
    }
}
