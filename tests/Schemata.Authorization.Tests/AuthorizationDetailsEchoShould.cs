using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Commands;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Security.Skeleton.Services;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class AuthorizationDetailsEchoShould
{
    private static readonly DateTime Anchor = new(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

    private static readonly TimeProvider Clock = new FixedClock(Anchor);


    private const string Issuer = "https://auth.example.com";

    private const string Grant =
        """[{"type":"payment_initiation","actions":["initiate"],"locations":["https://example.com/payments"]}]""";

    [Fact]
    public async Task Approve_PersistsTheGrantedDetailsOnTheConsentRecordAndTheSignInFerry() {
        var f      = CreateInteractionFixture(Grant);
        var result = await f.Handler.ApproveAsync(
                         new() { Code = "interact-ref" }, CreatePrincipal(), Issuer, CancellationToken.None);

        var created = CapturedAuthorization(f.AuthzMgr);
        Assert.Equal(Grant, created.AuthorizationDetails);
        Assert.Equal(Grant, result.Properties![Properties.AuthorizationDetails]);
        Assert.Equal("authorizations/auth-generated", result.Properties![Properties.AuthorizationName]);
    }

    [Fact]
    public async Task Approve_LeavesTheConsentRecordAndFerryUntouched_WhenNoDetailsRequested() {
        var f      = CreateInteractionFixture(null);
        var result = await f.Handler.ApproveAsync(
                         new() { Code = "interact-ref" }, CreatePrincipal(), Issuer, CancellationToken.None);

        Assert.Null(CapturedAuthorization(f.AuthzMgr).AuthorizationDetails);
        Assert.Null(result.Properties![Properties.AuthorizationDetails]);
    }


    [Fact]
    public async Task AutoApprove_PersistsTheGrantedDetailsOnTheConsentRecordAndTheSignInFerry() {
        var authzMgr = new Mock<IAuthorizationManager<SchemataAuthorization>>();
        authzMgr.Setup(m => m.CreateAsync(It.IsAny<SchemataAuthorization>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((SchemataAuthorization a, CancellationToken _) => a);
        var advisor = new AdviceAuthorizeAutoApproveSignIn<SchemataApplication, SchemataAuthorization>(
            Options.Create(new SchemataAuthorizationOptions { SessionIdClaimType = "sid" }),
            authzMgr.Object);
        var ctx   = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        ctx.Set(new AuthorizationDetailsGrant(Grant));
        var authz = new AuthorizeContext<SchemataApplication> {
            Application     = new() {
                Uid = Guid.NewGuid(), ClientId = "app-1", Name = "app-1", CanonicalName = "applications/app-1",
            },
            Request         = new() { Scope = "openid" },
            Principal       = CreatePrincipal(),
            ConsentDecision = ConsentDecision.Granted,
        };

        var result = await advisor.AdviseAsync(ctx, authz);

        Assert.Equal(AdviseResult.Handle, result);
        Assert.True(ctx.TryGet<AuthorizationResult>(out var signIn));
        var invocation = Assert.Single(
            authzMgr.Invocations,
            i => i.Method.Name == nameof(IAuthorizationManager<SchemataAuthorization>.CreateAsync));
        var created = Assert.IsType<SchemataAuthorization>(invocation.Arguments[0]);
        Assert.Equal(Grant, created.AuthorizationDetails);
        Assert.Equal(Grant, signIn!.Properties![Properties.AuthorizationDetails]);
    }

    [Fact]
    public async Task CarryTheGrantSetIntoTheCodePayload() {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var (service, tokens) = CreateSignInService(provider);
        SchemataToken? created = null;
        tokens.Setup(value => value.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()))
              .Callback((SchemataToken token, CancellationToken _) => created = token)
              .ReturnsAsync((SchemataToken? token, CancellationToken _) => token);
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity([new(IdentityClaims.Subject, "user-1")], "authorize"));

        await service.IssueAsync(principal, new Dictionary<string, string?> {
            [Properties.ResponseType]        = ResponseTypes.Code,
            [Properties.RedirectUri]         = "https://client.example/callback",
            [Properties.Scope]               = Scopes.OpenId,
            [Properties.AuthorizationDetails] = Grant,
        }, AuthorizationSignInResponseKind.Callback);

        var code = JsonSerializer.Deserialize<AuthorizationCodePayload>(created!.Payload!)!.Request;
        Assert.Equal(Grant, code!.AuthorizationDetails);
    }

    [Fact]
    public async Task RestoreTheGrantSetOnTheCodeExchangeFerry() {
        var payload = JsonSerializer.Serialize(new AuthorizationCodePayload {
            Request = new() {
                ClientId             = "test-client",
                RedirectUri          = "https://example.com/callback",
                Scope                = "openid",
                AuthorizationDetails = Grant,
            },
        });
        var token = new SchemataToken {
            Uid         = Guid.NewGuid(),
            Type        = TokenTypes.AuthorizationCode,
            Status      = TokenStatuses.Valid,
            ExpireTime  = Anchor.AddMinutes(5),
            Parent      = "user-1",
            Application = "applications/test-client",
            Payload     = payload,
        };
        var tokens = new Mock<ITokenStore<SchemataToken>>();
        tokens.Setup(t => t.FindByReferenceIdAsync("auth-code", It.IsAny<CancellationToken>())).ReturnsAsync(token);
        tokens.Setup(t => t.TryRedeemAsync(token, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var app = new SchemataApplication {
            Uid = Guid.NewGuid(), ClientId = "test-client", CanonicalName = "applications/test-client",
        };
        var clientAuth = new Mock<IClientAuthenticationService<SchemataApplication>>();
        clientAuth.Setup(c => c.AuthenticateAsync(It.IsAny<Dictionary<string, List<string?>>?>(),
                                                  It.IsAny<Dictionary<string, List<string?>>?>(),
                                                  It.IsAny<Dictionary<string, List<string?>>?>(),
                                                  It.IsAny<CancellationToken>()))
                  .ReturnsAsync(app);

        var services = new ServiceCollection();
        services.AddSingleton(tokens.Object);
        services.AddSingleton<ICodeExchangeAdvisor<SchemataApplication>>(
            new AdviceCodeExchangeValidation<SchemataApplication>(tokens.Object, Clock));
        using var sp      = services.BuildServiceProvider();
        using var ambient = AdviceContext.Establish(new(sp));

        var handler = new AuthorizationCodeHandler<SchemataApplication>(
            clientAuth.Object, tokens.Object, Options.Create(new JsonSerializerOptions()),
            Options.Create(new CodeFlowOptions()));

        var result = await handler.HandleAsync(new() {
            GrantType   = GrantTypes.AuthorizationCode,
            Code        = "auth-code",
            ClientId    = "test-client",
            RedirectUri = "https://example.com/callback",
        }, null, CancellationToken.None);

        Assert.Equal(AuthorizationStatus.SignIn, result.Status);
        Assert.Equal(Grant, result.Properties![Properties.AuthorizationDetails]);
    }

    [Fact]
    public async Task MintTheGrantSetAsAnAccessTokenClaim() {
        using var provider = new ServiceCollection()
                            .AddSingleton<IClaimsAdvisor>(new AdviceClaimsAudience(
                                Options.Create(new SchemataAuthorizationOptions { Issuer = "https://issuer.example" })))
                            .BuildServiceProvider();
        var (service, tokens) = CreateSignInService(provider);
        tokens.Setup(value => value.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((SchemataToken? token, CancellationToken _) => token);
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity([new(IdentityClaims.Subject, "user-1")], "grant"));

        var result = await service.IssueAsync(principal, new Dictionary<string, string?> {
            [Properties.GrantType]           = GrantTypes.AuthorizationCode,
            [Properties.Scope]               = "api",
            [Properties.Resources]           = "https://example.com/payments",
            [Properties.AuthorizationDetails] = Grant,
        }, AuthorizationSignInResponseKind.Token);

        var at = new JsonWebTokenHandler().ReadJsonWebToken(result.Token!.AccessToken!);
        Assert.True(at.TryGetPayloadValue<JsonElement>(Claims.AuthorizationDetails, out var claim));
        Assert.Equal(JsonValueKind.Array, claim.ValueKind);
        Assert.Equal("payment_initiation", claim[0].GetProperty("type").GetString());
    }

    [Fact]
    public async Task KeepTheGrantSetOutOfTheIdToken() {
        using var provider = new ServiceCollection()
                            .AddSingleton<IClaimsAdvisor>(new AdviceClaimsAudience(
                                Options.Create(new SchemataAuthorizationOptions { Issuer = "https://issuer.example" })))
                            .BuildServiceProvider();
        var (service, tokens) = CreateSignInService(provider);
        tokens.Setup(value => value.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((SchemataToken? token, CancellationToken _) => token);
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity([
                new(IdentityClaims.Subject, "user-1"),
                new(Claims.ClientId,        "client-1"),
            ], "grant"));

        var result = await service.IssueAsync(principal, new Dictionary<string, string?> {
            [Properties.GrantType]           = GrantTypes.AuthorizationCode,
            [Properties.Scope]               = $"{Scopes.OpenId} api",
            [Properties.AuthorizationDetails] = Grant,
        }, AuthorizationSignInResponseKind.Token);

        var id = new JsonWebTokenHandler().ReadJsonWebToken(result.Token!.IdToken!);
        Assert.False(id.TryGetPayloadValue<JsonElement>(Claims.AuthorizationDetails, out var _));
    }

    [Fact]
    public async Task EchoTheDetailsMatchingTheIntrospectedTokenAudience() {
        var f        = CreateIntrospectionFixture();
        using var ambient = AdviceContext.Establish(new(f.Sp));
        var details =
            """
            [
              {"type":"payment_initiation","actions":["initiate"],"locations":["https://example.com/payments"]},
              {"type":"account_information","actions":["read"],"locations":["https://example.com/accounts"]},
              {"type":"medical_record","actions":["read"]}
            ]
            """;
        var jwt    = await MintAccessToken(f.Issuer, ["https://example.com/payments"], details);
        var entity = CreateTokenEntity(jwt);
        f.Tokens.Setup(m => m.FindByReferenceIdAsync(jwt, It.IsAny<CancellationToken>())).ReturnsAsync(entity);

        var response = await f.Handler.HandleAsync(new() { Token = jwt }, null, CancellationToken.None);

        Assert.True(response.Active);
        var echoed = JsonNode.Parse(response.AuthorizationDetails!)!.AsArray();
        Assert.Equal(2, echoed.Count);
        Assert.Equal("payment_initiation", echoed[0]!["type"]!.GetValue<string>());
        Assert.Equal("medical_record",     echoed[1]!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task OmitTheDetailsOnIntrospection_WhenNoLocationMatchesTheAudience() {
        var       f       = CreateIntrospectionFixture();
        using var ambient = AdviceContext.Establish(new(f.Sp));
        var       details = """[{"type":"account_information","actions":["read"],"locations":["https://example.com/accounts"]}]""";
        var       jwt     = await MintAccessToken(f.Issuer, ["https://example.com/payments"], details);
        var       entity  = CreateTokenEntity(jwt);
        f.Tokens.Setup(m => m.FindByReferenceIdAsync(jwt, It.IsAny<CancellationToken>())).ReturnsAsync(entity);

        var response = await f.Handler.HandleAsync(new() { Token = jwt }, null, CancellationToken.None);

        Assert.True(response.Active);
        Assert.Null(response.AuthorizationDetails);
    }

    [Fact]
    public async Task OmitTheDetailsOnIntrospection_WhenTheTokenCarriesNone() {
        var       f       = CreateIntrospectionFixture();
        using var ambient = AdviceContext.Establish(new(f.Sp));
        var       jwt     = await MintAccessToken(f.Issuer, ["https://example.com/payments"], null);
        var       entity  = CreateTokenEntity(jwt);
        f.Tokens.Setup(m => m.FindByReferenceIdAsync(jwt, It.IsAny<CancellationToken>())).ReturnsAsync(entity);

        var response = await f.Handler.HandleAsync(new() { Token = jwt }, null, CancellationToken.None);

        Assert.True(response.Active);
        Assert.Null(response.AuthorizationDetails);
    }

    private static SchemataAuthorization CapturedAuthorization(Mock<IAuthorizationManager<SchemataAuthorization>> authzMgr) {
        var invocation = Assert.Single(
            authzMgr.Invocations,
            i => i.Method.Name == nameof(IAuthorizationManager<SchemataAuthorization>.CreateAsync));
        return Assert.IsType<SchemataAuthorization>(invocation.Arguments[0]);
    }

    // The fixture stores the normalized grant set, as the authorize leg stamps it onto the
    // interaction payload after the validating advisor accepts the parameter.
    private static InteractionFixture CreateInteractionFixture(string? details) {
        var jsonOpts = Options.Create(new JsonSerializerOptions());
        var authOpts = Options.Create(new SchemataAuthorizationOptions { SessionIdClaimType = "sid" });

        var app = new SchemataApplication {
            Uid           = Guid.NewGuid(),
            ClientId      = "browser-client",
            Name          = "browser-client",
            CanonicalName = "applications/browser-client",
        };
        var apps = new Mock<IApplicationManager<SchemataApplication>>();
        apps.Setup(a => a.FindByClientIdAsync("browser-client", It.IsAny<CancellationToken>())).ReturnsAsync(app);

        var payload = JsonSerializer.Serialize(new AuthorizeRequest {
            ClientId             = "browser-client",
            RedirectUri          = "https://localhost/callback",
            ResponseType         = ResponseTypes.Code,
            Scope                = "openid",
            AuthorizationDetails = details,
        }, jsonOpts.Value);

        var interaction = new SchemataToken {
            Uid         = Guid.NewGuid(),
            Name        = "interact-1",
            Type        = TokenTypes.Interaction,
            Status      = TokenStatuses.Valid,
            ReferenceId = "interact-ref",
            Payload     = payload,
            ExpireTime  = Anchor.AddMinutes(10),
        };

        var tokens = new Mock<ITokenStore<SchemataToken>>();
        tokens.Setup(t => t.FindByReferenceIdAsync("interact-ref", It.IsAny<CancellationToken>()))
              .ReturnsAsync(interaction);

        var authzMgr = new Mock<IAuthorizationManager<SchemataAuthorization>>();
        authzMgr.Setup(m => m.CreateAsync(It.IsAny<SchemataAuthorization>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((SchemataAuthorization a, CancellationToken _) => {
                     a.Name          = "auth-generated";
                     a.CanonicalName = "authorizations/auth-generated";
                     return a;
                 });

        var handler = new AuthorizeInteractionHandler<SchemataApplication, SchemataAuthorization, SchemataScope>(
            apps.Object, authzMgr.Object, new Mock<IScopeManager<SchemataScope>>().Object, tokens.Object,
            jsonOpts, authOpts, null, Clock);

        return new(handler, authzMgr);
    }

    private static ClaimsPrincipal CreatePrincipal(string subject = "users/u-42", string sid = "sess-99") {
        return new(new ClaimsIdentity([new(IdentityClaims.Subject, subject), new("sid", sid)], "test"));
    }

    private static (
        AuthorizationSignInService<SchemataApplication> Service,
        Mock<ITokenStore<SchemataToken>>                             Tokens
    ) CreateSignInService(IServiceProvider provider) {
        var options = new SchemataAuthorizationOptions {
            Issuer            = "https://issuer.example",
            AccessTokenFormat = TokenFormats.Jwt,
        };
        var tokens  = new Mock<ITokenStore<SchemataToken>>();
        var service = new AuthorizationSignInService<SchemataApplication>(
            Options.Create(options),
            Options.Create(new JsonSerializerOptions()),
            TestSecurityKeys.CreateTokenService(options),
            new Mock<IApplicationManager<SchemataApplication>>().Object,
            tokens.Object,
            provider);
        return (service, tokens);
    }

    private static (
        IntrospectionHandler<SchemataApplication> Handler,
        Mock<ITokenStore<SchemataToken>>                       Tokens,
        TokenService                                             Issuer,
        IServiceProvider                                         Sp
    ) CreateIntrospectionFixture() {
        var opts = Options.Create(new SchemataAuthorizationOptions { Issuer = Issuer });

        var tokens = new Mock<ITokenStore<SchemataToken>>();
        var issuer = TestSecurityKeys.CreateTokenService(opts.Value);

        var app        = new SchemataApplication { Uid = Guid.NewGuid(), ClientId = "resource-server" };
        var clientAuth = new Mock<IClientAuthenticationService<SchemataApplication>>();
        clientAuth.Setup(c => c.AuthenticateAsync(It.IsAny<Dictionary<string, List<string?>>?>(),
                                                  It.IsAny<Dictionary<string, List<string?>>?>(),
                                                  It.IsAny<Dictionary<string, List<string?>>?>(),
                                                  It.IsAny<CancellationToken>()))
                  .ReturnsAsync(app);

        var services = new ServiceCollection();
        services.TryAddEnumerable(ServiceDescriptor
                                 .Scoped<IIntrospectionAdvisor<SchemataApplication>,
                                          AdviceIntrospectionTokenValidation<SchemataApplication>>());
        services.TryAddEnumerable(ServiceDescriptor
                                 .Scoped<IIntrospectionAdvisor<SchemataApplication>,
                                          AdviceIntrospectionAuthorizationDetails<SchemataApplication>>());
        var sp = services.BuildServiceProvider();

        var handler = new IntrospectionHandler<SchemataApplication>(
            clientAuth.Object, issuer, tokens.Object);
        return (handler, tokens, issuer, sp);
    }

    private static async Task<string> MintAccessToken(TokenService issuer, string[] audiences, string? details) {
        var claims = new List<Claim> {
            new(Claims.JwtId, Guid.NewGuid().ToString()),
            new(IdentityClaims.Subject, "users/u-42"),
            new(Claims.ClientId, "test-client"),
            new(Claims.Scope, "openid"),
            new(Claims.Issuer, Issuer),
        };
        foreach (var audience in audiences) {
            claims.Add(new(Claims.Audience, audience));
        }

        if (details is not null) {
            claims.Add(new(Claims.AuthorizationDetails, details, JsonClaimValueTypes.Json));
        }

        return await issuer.CreateToken(claims, TimeSpan.FromHours(1));
    }

    private static SchemataToken CreateTokenEntity(string referenceId) {
        return new() {
            Uid         = Guid.NewGuid(),
            Type        = TokenTypes.AccessToken,
            Application = "resource-server",
            ReferenceId = referenceId,
            Payload     = referenceId,
            Format      = "jwt",
            Status      = TokenStatuses.Valid,
            ExpireTime  = Anchor.AddHours(1),
        };
    }

    #region Nested type: InteractionFixture

    private sealed record InteractionFixture(
        AuthorizeInteractionHandler<SchemataApplication, SchemataAuthorization, SchemataScope> Handler,
        Mock<IAuthorizationManager<SchemataAuthorization>>                                                    AuthzMgr
    );

    #endregion

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() { return now; }
    }
}
