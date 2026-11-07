using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Security.Skeleton.Entities;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class StepUpAuthenticationShould
{
    private const string Password = "urn:example:password";
    private const string Multifactor = "urn:example:mfa";
    private const string Resource = "https://api.example.com";
    private static readonly DateTimeOffset Anchor = new(2026, 11, 5, 0, 0, 0, TimeSpan.Zero);
    private static readonly ResourceAuthenticationRequirement Requirement = new([Multifactor], TimeSpan.FromSeconds(300));

    [Fact]
    public void Accept_Evidence_At_The_Max_Age_Boundary() {
        var authTime = Anchor.AddSeconds(-300).ToUnixTimeSeconds();

        Assert.True(Requirement.IsSatisfiedBy(Multifactor, authTime, Anchor));
    }

    [Fact]
    public void Reject_A_Different_Authentication_Class() {
        var authTime = Anchor.AddMinutes(-1).ToUnixTimeSeconds();

        Assert.False(Requirement.IsSatisfiedBy(Password, authTime, Anchor));
    }

    [Fact]
    public void Reject_Authentication_Older_Than_Max_Age() {
        var authTime = Anchor.AddSeconds(-301).ToUnixTimeSeconds();

        Assert.False(Requirement.IsSatisfiedBy(Multifactor, authTime, Anchor));
    }

    [Fact]
    public void Reject_Missing_Or_Future_Authentication_Time() {
        var future = Anchor.AddSeconds(1).ToUnixTimeSeconds();

        Assert.False(Requirement.IsSatisfiedBy(Multifactor, null, Anchor));
        Assert.False(Requirement.IsSatisfiedBy(Multifactor, future, Anchor));
    }

    [Fact]
    public void Render_Bearer_And_Dpop_Requirement_Challenges() {
        var bearer = SchemataAuthenticationHandler<SchemataApplication, SchemataSecurity, SchemataToken>
            .BuildBearerChallenge(OAuthErrors.InsufficientUserAuthentication, requirement: Requirement);
        var dpop = SchemataAuthenticationHandler<SchemataApplication, SchemataSecurity, SchemataToken>
            .BuildChallenge(OAuthErrors.InsufficientUserAuthentication, ["RS256", "ES256"], Requirement);

        Assert.StartsWith($"Bearer error=\"{OAuthErrors.InsufficientUserAuthentication}\"", bearer, StringComparison.Ordinal);
        Assert.Contains($"acr_values=\"{Multifactor}\"", bearer, StringComparison.Ordinal);
        Assert.Contains("max_age=\"300\"", bearer, StringComparison.Ordinal);
        Assert.StartsWith($"DPoP error=\"{OAuthErrors.InsufficientUserAuthentication}\"", dpop, StringComparison.Ordinal);
        Assert.Contains("algs=\"ES256 RS256\"", dpop, StringComparison.Ordinal);
        Assert.Contains($"acr_values=\"{Multifactor}\"", dpop, StringComparison.Ordinal);
        Assert.Contains("max_age=\"300\"", dpop, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Require_Reauthentication_And_Forward_Resource_Requirements() {
        var contexts = Contexts(new(Password, [], Anchor.AddHours(-1).ToUnixTimeSeconds()));
        var requirements = Requirements(Requirement);
        var advisor = new AdviceAuthorizeClaims<SchemataApplication>(
            contexts.Object, Options.Create(new SchemataAuthorizationOptions()), requirements.Object, new FixedClock(Anchor));
        using var services = new ServiceCollection().BuildServiceProvider();
        var authz = new AuthorizeContext<SchemataApplication> {
            Principal = Principal(),
            Application = new(),
            Request = new() {
                Resource = [Resource],
                AcrValues = Password,
                MaxAge = "900",
            },
        };

        var result = await advisor.AdviseAsync(new(services), authz);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.True(authz.RequireReauthentication);
        Assert.Equal($"{Password} {Multifactor}", authz.Request.AcrValues);
        Assert.Equal("300", authz.Request.MaxAge);
    }

    [Fact]
    public async Task Return_Unmet_Requirements_For_A_Silent_Request() {
        var contexts = Contexts(new(Password, [], Anchor.AddMinutes(-1).ToUnixTimeSeconds()));
        var requirements = Requirements(Requirement);
        var advisor = new AdviceAuthorizeClaims<SchemataApplication>(
            contexts.Object, Options.Create(new SchemataAuthorizationOptions()), requirements.Object, new FixedClock(Anchor));
        using var services = new ServiceCollection().BuildServiceProvider();
        var authz = new AuthorizeContext<SchemataApplication> {
            Principal = Principal(),
            Application = new(),
            Request = new() {
                Prompt = PromptValues.None,
                Prompts = new([PromptValues.None], StringComparer.Ordinal),
                Resource = [Resource],
                State = "xyz",
            },
        };

        var exception = await Assert.ThrowsAsync<OAuthException>(() => advisor.AdviseAsync(new(services), authz));

        Assert.Equal(OAuthErrors.UnmetAuthenticationRequirements, exception.Status);
        Assert.Equal("xyz", exception.State);
    }

    [Fact]
    public async Task Preserve_Voluntary_Acr_When_The_Provider_Returns_No_Requirement() {
        var contexts = Contexts(new(Password, [], Anchor.AddMinutes(-1).ToUnixTimeSeconds()));
        var requirements = Requirements(null);
        var advisor = new AdviceAuthorizeClaims<SchemataApplication>(
            contexts.Object, Options.Create(new SchemataAuthorizationOptions()), requirements.Object, new FixedClock(Anchor));
        using var services = new ServiceCollection().BuildServiceProvider();
        var authz = new AuthorizeContext<SchemataApplication> {
            Principal = Principal(),
            Application = new(),
            Request = new() {
                Resource = [Resource],
                AcrValues = Multifactor,
                MaxAge = "900",
            },
        };

        var result = await advisor.AdviseAsync(new(services), authz);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.False(authz.RequireReauthentication);
        Assert.Equal(Multifactor, authz.Request.AcrValues);
        Assert.Equal("900", authz.Request.MaxAge);
    }

    private static ClaimsPrincipal Principal() {
        return new(new ClaimsIdentity([new(IdentityClaims.Subject, "users/u-1")], "test"));
    }

    private static Mock<IAuthenticationContextProvider> Contexts(AuthenticationContext context) {
        var contexts = new Mock<IAuthenticationContextProvider>();
        contexts.Setup(value => value.GetContextAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(context);
        return contexts;
    }

    private static Mock<IResourceAuthenticationRequirementProvider> Requirements(ResourceAuthenticationRequirement? requirement) {
        var requirements = new Mock<IResourceAuthenticationRequirementProvider>();
        requirements.Setup(value => value.EvaluateAsync(It.IsAny<ResourceAuthenticationRequirementContext>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(requirement);
        return requirements;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() { return now; }
    }
}
