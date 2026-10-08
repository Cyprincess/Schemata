using System;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;
using Schemata.Authorization.Skeleton.Services;

namespace Schemata.Authorization.Tests;

public class AuthorizeInteractionHandlerSecurityShould
{
    private static readonly DateTime Anchor = new(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

    private const string Issuer = "https://auth.example.com";

    private const string InteractionCode = "interact-ref";

    private const string AccessTokenReference = "access-ref";

    [Fact]
    public async Task Deny_Revokes_Only_The_Interaction_Token_And_Leaves_The_Access_Token_Reference_Valid() {
        var (handler, tokens, _, interaction, accessToken) = CreateFixture();

        await handler.DenyAsync(new() { Code = InteractionCode }, CancellationToken.None);

        Assert.Equal(TokenStatuses.Revoked, interaction.Status);
        Assert.Equal(TokenStatuses.Valid, accessToken.Status);
        tokens.Verify(t => t.RevokeAsync(interaction, It.IsAny<CancellationToken>()), Times.Once);
        tokens.Verify(t => t.RevokeAsync(accessToken, It.IsAny<CancellationToken>()), Times.Never);
        tokens.Verify(t => t.FindByReferenceIdAsync(AccessTokenReference, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Approve_Second_Attempt_With_The_Same_Interaction_Token_Fails_As_Invalid_Grant() {
        var (handler, tokens, authzMgr, _, _) = CreateFixture();
        var principal = CreatePrincipal();

        await handler.ApproveAsync(new() { Code = InteractionCode }, principal, Issuer, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.ApproveAsync(
                                                             new() { Code = InteractionCode }, principal, Issuer,
                                                             CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidGrant, ex.Status);
        tokens.Verify(t => t.RevokeAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()), Times.Once);
        authzMgr.Verify(m => m.CreateAsync(It.IsAny<SchemataAuthorization>(), It.IsAny<CancellationToken>()), Times.Once);
    }
    [Fact]
    public async Task Reject_Approval_When_Required_AuthTime_Is_Missing() {
        var (handler, tokens, authzMgr, _, _) = CreateFixture(requireAuthTime: true);

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.ApproveAsync(
            new() { Code = InteractionCode }, CreatePrincipal(), Issuer, CancellationToken.None));

        Assert.Equal(OAuthErrors.LoginRequired, ex.Status);
        tokens.Verify(t => t.RevokeAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()), Times.Never);
        authzMgr.Verify(m => m.CreateAsync(It.IsAny<SchemataAuthorization>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reject_Approval_When_Final_Authentication_Exceeds_MaxAge() {
        var (handler, tokens, authzMgr, _, _) = CreateFixture(maxAge: "60");
        var principal = CreatePrincipal(authTime: new DateTimeOffset(Anchor, TimeSpan.Zero).AddSeconds(-61).ToUnixTimeSeconds());

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.ApproveAsync(
            new() { Code = InteractionCode }, principal, Issuer, CancellationToken.None));

        Assert.Equal(OAuthErrors.LoginRequired, ex.Status);
        tokens.Verify(t => t.RevokeAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()), Times.Never);
        authzMgr.Verify(m => m.CreateAsync(It.IsAny<SchemataAuthorization>(), It.IsAny<CancellationToken>()), Times.Never);
    }
    [Fact]
    public async Task Accept_Approval_After_Consent_When_Authentication_Satisfies_The_Required_Event_Boundary() {
        var boundary = new DateTimeOffset(Anchor, TimeSpan.Zero).AddSeconds(-60).ToUnixTimeSeconds();
        var (handler, tokens, authzMgr, interaction, _) = CreateFixture(maxAge: "0");
        var request = JsonSerializer.Deserialize<AuthorizeRequest>(interaction.Payload!)!;
        request.AuthenticationRequiredAfter = boundary;
        interaction.Payload = JsonSerializer.Serialize(request);

        await handler.ApproveAsync(
            new() { Code = InteractionCode }, CreatePrincipal(authTime: boundary + 1), Issuer, CancellationToken.None);

        tokens.Verify(t => t.RevokeAsync(interaction, It.IsAny<CancellationToken>()), Times.Once);
        authzMgr.Verify(m => m.CreateAsync(It.IsAny<SchemataAuthorization>(), It.IsAny<CancellationToken>()), Times.Once);
    }


    [Fact]
    public async Task Reject_Approval_When_Final_Acr_Does_Not_Satisfy_Essential_Claim() {
        const string claims = """{"id_token":{"acr":{"essential":true,"values":["urn:example:acr:mfa"]}}}""";
        var (handler, tokens, authzMgr, _, _) = CreateFixture(claims: claims);

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.ApproveAsync(
            new() { Code = InteractionCode }, CreatePrincipal(acr: "urn:example:acr:pwd"), Issuer, CancellationToken.None));

        Assert.Equal(OAuthErrors.LoginRequired, ex.Status);
        tokens.Verify(t => t.RevokeAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()), Times.Never);
        authzMgr.Verify(m => m.CreateAsync(It.IsAny<SchemataAuthorization>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reject_Approval_When_The_Final_Subject_Does_Not_Match_The_Requested_Subject() {
        const string claims = """{"id_token":{"sub":{"value":"users/u-42"}}}""";
        var (handler, tokens, authzMgr, _, _) = CreateFixture(claims: claims);

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.ApproveAsync(
            new() { Code = InteractionCode }, CreatePrincipal(subject: "users/u-7"), Issuer, CancellationToken.None));

        Assert.Equal(OAuthErrors.LoginRequired, ex.Status);
        tokens.Verify(t => t.RevokeAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()), Times.Never);
        authzMgr.Verify(m => m.CreateAsync(It.IsAny<SchemataAuthorization>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reject_Approval_When_The_Userinfo_Requested_Subject_Does_Not_Match() {
        const string claims = """{"userinfo":{"sub":{"value":"users/u-42"}}}""";
        var (handler, tokens, authzMgr, _, _) = CreateFixture(claims: claims);

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.ApproveAsync(
            new() { Code = InteractionCode }, CreatePrincipal(subject: "users/u-7"), Issuer, CancellationToken.None));

        Assert.Equal(OAuthErrors.LoginRequired, ex.Status);
        authzMgr.Verify(m => m.CreateAsync(It.IsAny<SchemataAuthorization>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Accept_Approval_When_The_Final_Subject_Matches_The_Requested_Subject() {
        const string claims = """{"id_token":{"sub":{"value":"users/u-42"}}}""";
        var (handler, tokens, authzMgr, _, _) = CreateFixture(claims: claims);

        await handler.ApproveAsync(
            new() { Code = InteractionCode }, CreatePrincipal(), Issuer, CancellationToken.None);

        tokens.Verify(t => t.RevokeAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()), Times.Once);
        authzMgr.Verify(m => m.CreateAsync(It.IsAny<SchemataAuthorization>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public async Task Skip_The_Claims_Approval_Rule_When_Its_Advisor_Is_Not_Installed() {
        const string claims = """{"id_token":{"acr":{"essential":true,"values":["urn:example:acr:mfa"]}}}""";
        var (handler, tokens, authzMgr, _, _) = CreateFixture(claims: claims, installClaimsAdvisor: false);

        await handler.ApproveAsync(
            new() { Code = InteractionCode }, CreatePrincipal(acr: "urn:example:acr:pwd"), Issuer, CancellationToken.None);

        tokens.Verify(t => t.RevokeAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()), Times.Once);
        authzMgr.Verify(m => m.CreateAsync(It.IsAny<SchemataAuthorization>(), It.IsAny<CancellationToken>()), Times.Once);
    }


    // The mocked store mirrors the row state machine the handler depends on: a revoked row keeps
    // its reference resolvable, but it no longer carries the Valid status the flow requires.
    private static (AuthorizeInteractionHandler<SchemataApplication, SchemataAuthorization, SchemataScope> Handler,
        Mock<ITokenStore<SchemataToken>>                                                      Tokens,
        Mock<IAuthorizationManager<SchemataAuthorization>>                                    AuthzMgr,
        SchemataToken                                                                         Interaction,
        SchemataToken                                                                         AccessToken) CreateFixture(
        bool requireAuthTime = false,
        string? maxAge = null,
        string? claims = null,
        bool installClaimsAdvisor = true
    ) {
        var jsonOpts = Options.Create(new JsonSerializerOptions());
        var authOpts = Options.Create(new SchemataAuthorizationOptions { SessionIdClaimType = "sid" });

        var app = new SchemataApplication {
            Uid           = Guid.NewGuid(),
            ClientId      = "browser-client",
            Name          = "browser-client",
            CanonicalName = "applications/browser-client",
        };
        app.RequireAuthTime = requireAuthTime;
        var apps = new Mock<IApplicationManager<SchemataApplication>>();
apps.SetupTypedMetadata();
        apps.Setup(a => a.FindByClientIdAsync("browser-client", It.IsAny<CancellationToken>())).ReturnsAsync(app);

        var payload = JsonSerializer.Serialize(new AuthorizeRequest {
            ClientId     = "browser-client",
            RedirectUri  = "https://localhost/callback",
            ResponseType = ResponseTypes.Code,
            Scope        = "openid",
            MaxAge       = maxAge,
            Claims       = claims,
        }, jsonOpts.Value);

        var interaction = new SchemataToken {
            Uid         = Guid.NewGuid(),
            Name        = "interact-security",
            Type        = TokenTypes.Interaction,
            Status      = TokenStatuses.Valid,
            ReferenceId = InteractionCode,
            Payload     = payload,
            ExpireTime  = Anchor.AddMinutes(10),
        };
        var accessToken = new SchemataToken {
            Uid         = Guid.NewGuid(),
            Name        = "access-security",
            Type        = TokenTypes.AccessToken,
            Status      = TokenStatuses.Valid,
            ReferenceId = AccessTokenReference,
            ExpireTime  = Anchor.AddHours(1),
        };

        var tokens = new Mock<ITokenStore<SchemataToken>>();
        tokens.Setup(t => t.FindByReferenceIdAsync(InteractionCode, It.IsAny<CancellationToken>()))
              .ReturnsAsync(interaction);
        tokens.Setup(t => t.RevokeAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()))
              .Callback((SchemataToken token, CancellationToken _) => token.Status = TokenStatuses.Revoked)
              .Returns(Task.CompletedTask);

        var authzMgr = new Mock<IAuthorizationManager<SchemataAuthorization>>();
        authzMgr.Setup(m => m.CreateAsync(It.IsAny<SchemataAuthorization>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((SchemataAuthorization a, CancellationToken _) => a);

        var advisors = installClaimsAdvisor
            ? new Schemata.Authorization.Skeleton.Advisors.IAuthorizeAdvisor<SchemataApplication>[] { new AdviceAuthorizeClaims<SchemataApplication>() }
            : null;
        var handler = new AuthorizeInteractionHandler<SchemataApplication, SchemataAuthorization, SchemataScope>(
            apps.Object, authzMgr.Object, new Mock<IScopeManager<SchemataScope>>().Object, tokens.Object,
            jsonOpts, authOpts, new FixedClock(Anchor), advisors: advisors);

        return (handler, tokens, authzMgr, interaction, accessToken);
    }

    private static ClaimsPrincipal CreatePrincipal(long? authTime = null, string? acr = null, string subject = "users/u-42") {
        var claims = new System.Collections.Generic.List<Claim> { new(IdentityClaims.Subject, subject) };
        if (authTime is not null) claims.Add(new(Claims.AuthTime, authTime.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (acr is not null) claims.Add(new(Claims.Acr, acr));
        return new(new ClaimsIdentity(claims, "test"));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() { return now; }
    }
}
