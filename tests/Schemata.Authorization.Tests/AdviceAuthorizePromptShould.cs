using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Services;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class AdviceAuthorizePromptShould
{
    private static readonly DateTimeOffset Anchor = new(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Last authentication one minute before the clock anchor: inside a 900-second max_age.</summary>
    private static readonly long FreshAuthTime = Anchor.AddMinutes(-1).ToUnixTimeSeconds();

    /// <summary>Last authentication two hours before the clock anchor: beyond any small max_age.</summary>
    private static readonly long StaleAuthTime = Anchor.AddHours(-2).ToUnixTimeSeconds();

    [Fact]
    public async Task Continue_When_Auth_Time_Is_Inside_The_Max_Age_Window() {
        var (advisor, ctx) = Create(FreshAuthTime);
        var authz = new AuthorizeContext<SchemataApplication> { Request = new() { MaxAge = "900" } };

        var result = await advisor.AdviseAsync(ctx, authz);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.False(authz.RequireReauthentication);
    }

    [Fact]
    public async Task Require_Reauthentication_When_Auth_Time_Exceeds_Max_Age() {
        var (advisor, ctx) = Create(StaleAuthTime);
        var authz = new AuthorizeContext<SchemataApplication> { Request = new() { MaxAge = "900" } };

        var result = await advisor.AdviseAsync(ctx, authz);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.True(authz.RequireReauthentication);
    }

    [Fact]
    public async Task Require_Reauthentication_When_Max_Age_Is_Zero() {
        var (advisor, ctx) = Create(FreshAuthTime);
        var authz = new AuthorizeContext<SchemataApplication> { Request = new() { MaxAge = "0" } };

        await advisor.AdviseAsync(ctx, authz);

        Assert.True(authz.RequireReauthentication);
    }

    [Fact]
    public async Task Reject_Max_Age_Beyond_The_Non_Negative_Integer_Range() {
        var (advisor, ctx) = Create(FreshAuthTime);
        var authz = new AuthorizeContext<SchemataApplication> { Request = new() { MaxAge = "abc" } };

        var ex = await Assert.ThrowsAsync<OAuthException>(() => advisor.AdviseAsync(ctx, authz));
        Assert.Equal(OAuthErrors.InvalidRequest, ex.Status);
    }

    [Fact]
    public async Task Continue_Without_Auth_Time_Evidence_When_Max_Age_Is_Asked() {
        var (advisor, ctx) = Create(null);
        var authz = new AuthorizeContext<SchemataApplication> { Request = new() { MaxAge = "900" } };

        var result = await advisor.AdviseAsync(ctx, authz);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.True(authz.RequireReauthentication);
    }

    [Fact]
    public async Task Raise_Login_Required_For_Expired_Session_Under_Prompt_None() {
        var (advisor, ctx) = Create(StaleAuthTime);
        var authz = new AuthorizeContext<SchemataApplication> {
            Request   = new() { Prompt = PromptValues.None, MaxAge = "900" },
            Principal = Authenticated(),
        };

        var ex = await Assert.ThrowsAsync<OAuthException>(() => advisor.AdviseAsync(ctx, authz));
        Assert.Equal(OAuthErrors.LoginRequired, ex.Status);
    }

    [Fact]
    public async Task Continue_On_Any_Requested_Acr_Values() {
        var (advisor, ctx) = Create(FreshAuthTime);
        var authz = new AuthorizeContext<SchemataApplication> {
            Request = new() {
                AcrValues = "urn:schemata:acr:classes:multifactor urn:example:acr:unknown",
            },
        };

        // Core §5.5.1.1: a voluntary acr request the OP cannot satisfy is never an error; the
        // login pipeline stamps the performed level instead.
        var result = await advisor.AdviseAsync(ctx, authz);

        Assert.Equal(AdviseResult.Continue, result);
    }

    private static ClaimsPrincipal Authenticated() {
        return new(new ClaimsIdentity([new(IdentityClaims.Subject, "users/u-1")], "test"));
    }

    private static (AdviceAuthorizePrompt<SchemataApplication> advisor, AdviceContext ctx) Create(long? authTime) {
        var contexts = new Mock<IAuthenticationContextProvider>();
        contexts
            .Setup(value => value.GetContextAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthenticationContext(null, Array.Empty<string>(), authTime));

        var sp      = new ServiceCollection().BuildServiceProvider();
        var advisor = new AdviceAuthorizePrompt<SchemataApplication>(contexts.Object, new FixedClock(Anchor));
        var ctx     = new AdviceContext(sp);
        return (advisor, ctx);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() { return now; }
    }
}
