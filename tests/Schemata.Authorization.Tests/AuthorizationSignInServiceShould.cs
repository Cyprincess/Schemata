using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Common;
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
            new Claim(IdentityClaims.Subject, "user-1"),
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
        Assert.Equal(TokenTypes.AccessToken, created!.Type);
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
            new Claim(IdentityClaims.Subject, "user-1"),
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
        Assert.Equal(TokenTypes.AuthorizationCode, created!.Type);
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
            new Claim(IdentityClaims.Subject, "user-1"),
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
            new Claim(IdentityClaims.Subject, "user-1"),
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
        AuthorizationSignInService<SchemataApplication, SchemataToken> Service,
        Mock<ITokenManager<SchemataToken>> Tokens
    ) Create(System.IServiceProvider provider) {
        var options = new SchemataAuthorizationOptions {
            Issuer            = "https://issuer.example",
            AccessTokenFormat = TokenFormats.Jwt,
        };
        options.AddEphemeralSigningKey();
        var tokens = new Mock<ITokenManager<SchemataToken>>();
        var apps   = new Mock<IApplicationManager<SchemataApplication>>();
        var service = new AuthorizationSignInService<SchemataApplication, SchemataToken>(
            Options.Create(options),
            Options.Create(new JsonSerializerOptions()),
            new TokenService(Options.Create(options)),
            apps.Object,
            tokens.Object,
            provider);
        return (service, tokens);
    }
}
