using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class AdviceResourceShould
{
    private const string TestClientId    = "test-client";
    private const string TestRedirectUri = "https://example.com/callback";
    private const string TestCode        = "auth-code";
    private const string ResourceA       = "https://cal.example.com/";
    private const string ResourceB       = "https://contacts.example.com/";
    private const string ResourceC       = "https://foreign.example.com/";

    private static readonly JsonSerializerOptions JsonOptions = new();

    #region AdviceAuthorizeResource

    public sealed class Authorize
    {
        private static (AdviceAuthorizeResource<SchemataApplication>, AdviceContext) Create() {
            var sp      = new ServiceCollection().BuildServiceProvider();
            var advisor = new AdviceAuthorizeResource<SchemataApplication>();
            return (advisor, new(sp));
        }

        [Theory]
        [InlineData("https://cal.example.com/")]
        [InlineData("https://cal.example.com/app?q=scim")]
        public async Task Accept_Well_Formed_Resource_Values(string resource) {
            var (advisor, ctx) = Create();
            var authz = new AuthorizeContext<SchemataApplication> { Request = new() { Resource = [resource] } };

            var result = await advisor.AdviseAsync(ctx, authz);

            Assert.Equal(AdviseResult.Continue, result);
        }

        [Theory]
        [InlineData("cal.example.com")]
        [InlineData("https://cal.example.com/#row")]
        [InlineData("urn:example:api")]
        [InlineData("foo:bar")]
        public async Task Reject_Malformed_Resource_Values_With_InvalidTarget(string resource) {
            var (advisor, ctx) = Create();
            var authz = new AuthorizeContext<SchemataApplication> {
                Request = new() {
                    Resource    = [resource],
                    RedirectUri = TestRedirectUri,
                    State       = "xyz",
                },
                ResponseMode = ResponseModes.Query,
            };

            var ex = await Assert.ThrowsAsync<OAuthException>(() => advisor.AdviseAsync(ctx, authz));

            Assert.Equal(OAuthErrors.InvalidTarget, ex.Status);
            Assert.Equal(TestRedirectUri, ex.RedirectUri);
            Assert.Equal("xyz", ex.State);
        }

        [Fact]
        public async Task Pass_Through_When_No_Resource_Is_Requested() {
            var (advisor, ctx) = Create();
            var authz = new AuthorizeContext<SchemataApplication> { Request = new() { ResponseType = "code" } };

            var result = await advisor.AdviseAsync(ctx, authz);

            Assert.Equal(AdviseResult.Continue, result);
        }
    }

    #endregion

    #region AdviceTokenResource

    public sealed class Token
    {
        private static (AdviceTokenResource<SchemataApplication>, AdviceContext) Create() {
            var sp      = new ServiceCollection().BuildServiceProvider();
            var advisor = new AdviceTokenResource<SchemataApplication>();
            return (advisor, new(sp));
        }

        [Fact]
        public async Task Adopt_The_Requested_Resources_For_Client_Credentials() {
            var (advisor, ctx) = Create();
            var request = new TokenRequest {
                GrantType = GrantTypes.ClientCredentials,
                Resource  = [ResourceA, ResourceB],
            };

            var result = await advisor.AdviseAsync(ctx, new(), request);

            Assert.Equal(AdviseResult.Continue, result);
            Assert.True(ctx.TryGet<ResourceIndicators>(out var adopted));
            Assert.Equal([ResourceA, ResourceB], adopted!.Values);
        }

        [Theory]
        [InlineData(GrantTypes.AuthorizationCode)]
        [InlineData(GrantTypes.RefreshToken)]
        public async Task Defer_Grant_Bound_Consistency_To_The_Grant_Handlers(string grantType) {
            var (advisor, ctx) = Create();
            var request = new TokenRequest {
                GrantType = grantType,
                Resource  = [ResourceA],
            };

            var result = await advisor.AdviseAsync(ctx, new(), request);

            Assert.Equal(AdviseResult.Continue, result);
            Assert.False(ctx.Has<ResourceIndicators>());
        }

        [Theory]
        [InlineData("urn:example:api")]
        [InlineData("https://cal.example.com/#row")]
        public async Task Reject_Malformed_Resource_Values_With_InvalidTarget(string resource) {
            var (advisor, ctx) = Create();
            var request = new TokenRequest {
                GrantType = GrantTypes.ClientCredentials,
                Resource  = [resource],
            };

            var ex = await Assert.ThrowsAsync<OAuthException>(
                () => advisor.AdviseAsync(ctx, new(), request));

            Assert.Equal(OAuthErrors.InvalidTarget, ex.Status);
            Assert.Null(ex.RedirectUri);
        }

        [Fact]
        public async Task Pass_Through_When_No_Resource_Is_Requested() {
            var (advisor, ctx) = Create();
            var request = new TokenRequest { GrantType = GrantTypes.ClientCredentials };

            var result = await advisor.AdviseAsync(ctx, new(), request);

            Assert.Equal(AdviseResult.Continue, result);
            Assert.False(ctx.Has<ResourceIndicators>());
        }
    }

    #endregion

    #region AuthorizationCodeHandler consistency

    public sealed class CodeExchange
    {
        private static readonly DateTimeOffset Now = new(2026, 8, 26, 0, 0, 0, TimeSpan.Zero);

        private static (
            AuthorizationCodeHandler<SchemataApplication> Handler,
            AdviceContext Ctx
        ) Create(AuthorizeRequest payload) {
            var token = new SchemataToken {
                Uid         = Guid.NewGuid(),
                Type        = TokenTypes.AuthorizationCode,
                Status      = TokenStatuses.Valid,
                ExpireTime  = Now.AddMinutes(5).UtcDateTime,
                Parent      = "user-1",
                Application = $"applications/{TestClientId}",
                ReferenceId = TestCode,
                Payload     = JsonSerializer.Serialize(new AuthorizationCodePayload { Request = payload }, JsonOptions),
            };

            var tokens = new Mock<ITokenStore<SchemataToken>>();
            tokens.Setup(t => t.FindByReferenceIdAsync(TestCode, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(token);
            tokens.Setup(t => t.TryRedeemAsync(token, It.IsAny<CancellationToken>())).ReturnsAsync(true);

            var app = new SchemataApplication {
                Uid           = Guid.NewGuid(),
                ClientId      = TestClientId,
                CanonicalName = $"applications/{TestClientId}",
            };
            var clientAuth = new Mock<IClientAuthenticationService<SchemataApplication>>();
            clientAuth.Setup(c => c.AuthenticateAsync(
                                  It.IsAny<Dictionary<string, List<string?>>?>(),
                                  It.IsAny<Dictionary<string, List<string?>>?>(),
                                  It.IsAny<Dictionary<string, List<string?>>?>(),
                                  It.IsAny<CancellationToken>()))
                      .ReturnsAsync(app);

            var services = new ServiceCollection();
            services.AddSingleton(tokens.Object);
            services.AddSingleton<ICodeExchangeAdvisor<SchemataApplication>>(
                new AdviceCodeExchangeValidation<SchemataApplication>(
                    tokens.Object, new FakeTimeProvider(Now)));
            var sp = services.BuildServiceProvider();

            var handler = new AuthorizationCodeHandler<SchemataApplication>(
                clientAuth.Object, tokens.Object, Options.Create(JsonOptions),
                Options.Create(new CodeFlowOptions()));

            return (handler, new(sp));
        }

        private static TokenRequest CreateRequest(ICollection<string>? resources = null) {
            return new() {
                GrantType   = GrantTypes.AuthorizationCode,
                Code        = TestCode,
                ClientId    = TestClientId,
                RedirectUri = TestRedirectUri,
                Resource    = resources,
            };
        }

        private static AuthorizeRequest CreatePayload(ICollection<string>? resources = null) {
            return new() {
                ClientId    = TestClientId,
                RedirectUri = TestRedirectUri,
                Scope       = "openid",
                Resource    = resources,
            };
        }

        [Fact]
        public async Task Exchange_With_The_Granted_Set_In_Any_Order_Succeeds() {
            var (handler, ctx) = Create(CreatePayload([ResourceA, ResourceB]));
            using var ambient = AdviceContext.Establish(ctx);

            var result = await handler.HandleAsync(CreateRequest([ResourceB, ResourceA]), null,
                CancellationToken.None);

            Assert.Equal(AuthorizationStatus.SignIn, result.Status);
            Assert.Equal($"{ResourceB} {ResourceA}", result.Properties![Properties.Resources]);
            Assert.True(ctx.TryGet<ResourceIndicators>(out var adopted));
            Assert.Equal([ResourceB, ResourceA], adopted!.Values);
        }

        [Fact]
        public async Task Exchange_With_A_Different_Set_Is_InvalidTarget() {
            var (handler, ctx) = Create(CreatePayload([ResourceA, ResourceB]));
            using var ambient = AdviceContext.Establish(ctx);

            var ex = await Assert.ThrowsAsync<OAuthException>(
                () => handler.HandleAsync(CreateRequest([ResourceA, ResourceC]), null, CancellationToken.None));

            Assert.Equal(OAuthErrors.InvalidTarget, ex.Status);
        }

        [Fact]
        public async Task Exchange_Without_Resources_Adopts_The_Granted_Set() {
            var (handler, ctx) = Create(CreatePayload([ResourceA, ResourceB]));
            using var ambient = AdviceContext.Establish(ctx);

            var result = await handler.HandleAsync(CreateRequest(), null, CancellationToken.None);

            Assert.Equal(AuthorizationStatus.SignIn, result.Status);
            Assert.Equal($"{ResourceA} {ResourceB}", result.Properties![Properties.Resources]);
            Assert.True(ctx.TryGet<ResourceIndicators>(out var adopted));
            Assert.Equal([ResourceA, ResourceB], adopted!.Values);
        }

        [Fact]
        public async Task Exchange_With_Resources_When_None_Granted_Is_InvalidTarget() {
            var (handler, ctx) = Create(CreatePayload());
            using var ambient = AdviceContext.Establish(ctx);

            var ex = await Assert.ThrowsAsync<OAuthException>(
                () => handler.HandleAsync(CreateRequest([ResourceA]), null, CancellationToken.None));

            Assert.Equal(OAuthErrors.InvalidTarget, ex.Status);
        }
    }

    #endregion

    #region RefreshTokenHandler consistency

    public sealed class Refresh
    {
        private static async Task<(RefreshTokenHandler<SchemataApplication> Handler, AdviceContext Ctx)>
            Create(string? granted = $"{ResourceA} {ResourceB}") {
            var authOpts = new SchemataAuthorizationOptions { Issuer = "https://auth.example.com" };
            var tokenService = TestSecurityKeys.CreateTokenService(authOpts);

            var claims = new List<Claim> {
                new(IdentityClaims.Subject, "user-1"),
                new(Claims.Scope, "openid offline_access"),
            };
            if (granted is not null) {
                claims.Add(new(Claims.Resources, granted));
            }

            var refreshToken = new SchemataToken {
                Uid         = Guid.NewGuid(),
                Type        = TokenTypes.RefreshToken,
                Status      = TokenStatuses.Valid,
                ReferenceId = "rt-ref",
                Payload     = await tokenService.CreateToken(claims, TimeSpan.FromHours(1)),
                Parent      = "user-1",
            };

            var tokens = new Mock<ITokenStore<SchemataToken>>();
            tokens.Setup(t => t.FindByReferenceIdAsync("rt-ref", It.IsAny<CancellationToken>()))
                  .ReturnsAsync(refreshToken);

            var app = new SchemataApplication { Uid = Guid.NewGuid(), ClientId = "test" };
            var clientAuth = new Mock<IClientAuthenticationService<SchemataApplication>>();
            clientAuth.Setup(c => c.AuthenticateAsync(
                                  It.IsAny<Dictionary<string, List<string?>>?>(),
                                  It.IsAny<Dictionary<string, List<string?>>?>(),
                                  It.IsAny<Dictionary<string, List<string?>>?>(),
                                  It.IsAny<CancellationToken>()))
                      .ReturnsAsync(app);

            var sp      = new ServiceCollection().BuildServiceProvider();
            var handler = new RefreshTokenHandler<SchemataApplication>(
                clientAuth.Object, tokens.Object, tokenService,
                Options.Create(new RefreshTokenFlowOptions()), sp);

            return (handler, new(sp));
        }

        private static TokenRequest CreateRequest(ICollection<string>? resources = null) {
            return new() {
                GrantType    = GrantTypes.RefreshToken,
                ClientId     = "test",
                RefreshToken = "rt-ref",
                Resource     = resources,
            };
        }

        [Fact]
        public async Task Refresh_With_A_Subset_Of_The_Granted_Resources_Succeeds() {
            var (handler, ctx) = await Create();
            using var ambient = AdviceContext.Establish(ctx);

            var result = await handler.HandleAsync(CreateRequest([ResourceA]), null, CancellationToken.None);

            Assert.Equal(AuthorizationStatus.SignIn, result.Status);
            Assert.Equal($"{ResourceA} {ResourceB}", result.Properties![Properties.Resources]);
            Assert.True(ctx.TryGet<ResourceIndicators>(out var adopted));
            Assert.Equal([ResourceA], adopted!.Values);
        }

        [Fact]
        public async Task Refresh_With_The_Full_Granted_Set_Succeeds() {
            var (handler, ctx) = await Create();
            using var ambient = AdviceContext.Establish(ctx);

            var result = await handler.HandleAsync(CreateRequest([ResourceB, ResourceA]), null,
                CancellationToken.None);

            Assert.Equal(AuthorizationStatus.SignIn, result.Status);
        }

        [Fact]
        public async Task Refresh_With_A_Foreign_Resource_Is_InvalidTarget() {
            var (handler, ctx) = await Create();
            using var ambient = AdviceContext.Establish(ctx);

            var ex = await Assert.ThrowsAsync<OAuthException>(
                () => handler.HandleAsync(CreateRequest([ResourceC]), null, CancellationToken.None));

            Assert.Equal(OAuthErrors.InvalidTarget, ex.Status);
        }

        [Fact]
        public async Task Refresh_Without_Resources_Adopts_The_Granted_Set() {
            var (handler, ctx) = await Create();
            using var ambient = AdviceContext.Establish(ctx);

            var result = await handler.HandleAsync(CreateRequest(), null, CancellationToken.None);

            Assert.Equal(AuthorizationStatus.SignIn, result.Status);
            Assert.True(ctx.TryGet<ResourceIndicators>(out var adopted));
            Assert.Equal([ResourceA, ResourceB], adopted!.Values);
        }

        [Fact]
        public async Task Refresh_With_Resources_When_None_Granted_Is_InvalidTarget() {
            var (handler, ctx) = await Create(granted: null);
            using var ambient = AdviceContext.Establish(ctx);

            var ex = await Assert.ThrowsAsync<OAuthException>(
                () => handler.HandleAsync(CreateRequest([ResourceA]), null, CancellationToken.None));

            Assert.Equal(OAuthErrors.InvalidTarget, ex.Status);
        }
    }

    #endregion
}
