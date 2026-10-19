using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class AdviceAuthorizeConsentShould
{
    [Fact]
    public async Task GrantConsent_WhenExistingAuthorizationCoversRequestedScopes() {
        var authorization = new SchemataAuthorization {
            Status = TokenStatuses.Valid, Type = AuthorizationTypes.Permanent, Scopes = "openid profile email",
        };
        var authzMgr = SetupAuthzMgr(authorization);

        var advisor = new AdviceAuthorizeConsent<SchemataApplication, SchemataAuthorization>(authzMgr.Object, new ExplicitConsentModelProvider());
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var authz = new AuthorizeContext<SchemataApplication> {
            Application = CreateApplication(),
            Request     = new() { Scope = "openid profile" },
            Principal   = CreatePrincipal("users/u-1"),
        };

        await advisor.AdviseAsync(ctx, authz);

        Assert.Equal(ConsentDecision.Granted, authz.ConsentDecision);
    }

    [Fact]
    public async Task NotGrantConsent_WhenRequestedScopesExceedGranted() {
        var authorization = new SchemataAuthorization { Status = TokenStatuses.Valid, Scopes = "openid profile" };
        var authzMgr      = SetupAuthzMgr(authorization);

        var advisor = new AdviceAuthorizeConsent<SchemataApplication, SchemataAuthorization>(authzMgr.Object, new ExplicitConsentModelProvider());
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var authz = new AuthorizeContext<SchemataApplication> {
            Application = CreateApplication(),
            Request     = new() { Scope = "openid profile email" },
            Principal   = CreatePrincipal("users/u-1"),
        };

        await advisor.AdviseAsync(ctx, authz);

        Assert.NotEqual(ConsentDecision.Granted, authz.ConsentDecision);
    }
    [Fact]
    public async Task Skip_Interactive_Consent_During_Par_Validation() {
        var authzMgr = new Mock<IAuthorizationManager<SchemataAuthorization>>();
        var advisor = new AdviceAuthorizeConsent<SchemataApplication, SchemataAuthorization>(
            authzMgr.Object, new ExplicitConsentModelProvider());
        var ctx = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var authz = new AuthorizeContext<SchemataApplication> {
            Stage = AuthorizationRequestStage.Pushed,
            Application = CreateApplication(),
            Request     = new() { Prompt = PromptValues.None, Scope = "openid" },
        };

        var result = await advisor.AdviseAsync(ctx, authz);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Equal(ConsentDecision.Pending, authz.ConsentDecision);
        authzMgr.Verify(m => m.ListAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GrantConsent_When_Prior_Grant_Covers_The_Requested_Authorization_Details() {
        const string details = """[{"type":"payment_initiation","actions":["list"]}]""";
        var authorization = new SchemataAuthorization {
            Status = TokenStatuses.Valid, Type = AuthorizationTypes.Permanent, Scopes = "openid",
            AuthorizationDetails = details,
        };

        var advisor = new AdviceAuthorizeConsent<SchemataApplication, SchemataAuthorization>(
            SetupAuthzMgr(authorization).Object, new ExplicitConsentModelProvider(), Details());
        var ctx = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var authz = new AuthorizeContext<SchemataApplication> {
            Application         = CreateApplication(),
            Request             = new() { Scope = "openid" },
            Principal           = CreatePrincipal("users/u-1"),
            AuthorizationDetails = details,
        };

        await advisor.AdviseAsync(ctx, authz);

        Assert.Equal(ConsentDecision.Granted, authz.ConsentDecision);
    }

    [Fact]
    public async Task GrantConsent_When_The_Request_Narrows_The_Prior_Authorization_Details() {
        var authorization = new SchemataAuthorization {
            Status = TokenStatuses.Valid, Type = AuthorizationTypes.Permanent, Scopes = "openid",
            AuthorizationDetails = """[{"type":"payment_initiation","actions":["list","write"]}]""",
        };

        var advisor = new AdviceAuthorizeConsent<SchemataApplication, SchemataAuthorization>(
            SetupAuthzMgr(authorization).Object, new ExplicitConsentModelProvider(), Details());
        var ctx = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var authz = new AuthorizeContext<SchemataApplication> {
            Application         = CreateApplication(),
            Request             = new() { Scope = "openid" },
            Principal           = CreatePrincipal("users/u-1"),
            AuthorizationDetails = """[{"type":"payment_initiation","actions":["list"]}]""",
        };

        await advisor.AdviseAsync(ctx, authz);

        Assert.Equal(ConsentDecision.Granted, authz.ConsentDecision);
    }

    [Fact]
    public async Task RequireConsent_When_Scope_Only_Prior_Consent_Meets_A_Rich_Authorization_Request() {
        var authorization = new SchemataAuthorization {
            Status = TokenStatuses.Valid, Type = AuthorizationTypes.Permanent, Scopes = "openid",
        };

        var advisor = new AdviceAuthorizeConsent<SchemataApplication, SchemataAuthorization>(
            SetupAuthzMgr(authorization).Object, new ExplicitConsentModelProvider(), Details());
        var ctx = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var authz = new AuthorizeContext<SchemataApplication> {
            Application         = CreateApplication(),
            Request             = new() { Scope = "openid" },
            Principal           = CreatePrincipal("users/u-1"),
            AuthorizationDetails = """[{"type":"payment_initiation","actions":["list"]}]""",
        };

        await advisor.AdviseAsync(ctx, authz);

        Assert.Equal(ConsentDecision.Required, authz.ConsentDecision);
    }

    [Fact]
    public async Task RequireConsent_When_The_Request_Expands_The_Prior_Authorization_Details() {
        var authorization = new SchemataAuthorization {
            Status = TokenStatuses.Valid, Type = AuthorizationTypes.Permanent, Scopes = "openid",
            AuthorizationDetails = """[{"type":"payment_initiation","actions":["list"]}]""",
        };

        var advisor = new AdviceAuthorizeConsent<SchemataApplication, SchemataAuthorization>(
            SetupAuthzMgr(authorization).Object, new ExplicitConsentModelProvider(), Details());
        var ctx = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var authz = new AuthorizeContext<SchemataApplication> {
            Application         = CreateApplication(),
            Request             = new() { Scope = "openid" },
            Principal           = CreatePrincipal("users/u-1"),
            AuthorizationDetails = """[{"type":"payment_initiation","actions":["list","write"]}]""",
        };

        await advisor.AdviseAsync(ctx, authz);

        Assert.Equal(ConsentDecision.Required, authz.ConsentDecision);
    }


    private static SchemataApplication CreateApplication(string clientId = "test-app") {
        return new() {
            ClientId      = clientId,
            Name          = clientId,
            CanonicalName = $"applications/{clientId}",
        };
    }

    private static ClaimsPrincipal CreatePrincipal(string subject) {
        return new(new ClaimsIdentity([new(IdentityClaims.Subject, subject)], "test"));
    }

    private static Mock<IAuthorizationManager<SchemataAuthorization>> SetupAuthzMgr(
        SchemataAuthorization authorization
    ) {
        var mock = new Mock<IAuthorizationManager<SchemataAuthorization>>();
        mock.Setup(m => m.ListAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(ToAsync(authorization));
        return mock;
    }

    private static AuthorizationDetailsService Details() {
        var descriptor = new Mock<IAuthorizationDetailTypeDescriptor>();
        descriptor.Setup(d => d.Type).Returns("payment_initiation");
        descriptor.Setup(d => d.Validate(It.IsAny<JsonElement>())).Returns((string?)null);
        descriptor.Setup(d => d.Narrow(It.IsAny<JsonElement>(), It.IsAny<JsonElement>()))
                  .Returns((JsonElement granted, JsonElement requested) => {
                       var grantedActions = granted.GetProperty("actions")
                                                   .EnumerateArray()
                                                   .Select(action => action.GetString())
                                                   .ToList();
                       var covered = requested.GetProperty("actions")
                                              .EnumerateArray()
                                              .All(action => grantedActions.Contains(action.GetString()));
                       return covered ? requested : (JsonElement?)null;
                   });
        return new([descriptor.Object]);
    }

#pragma warning disable CS1998
    private static async IAsyncEnumerable<T> ToAsync<T>(T item) { yield return item; }
#pragma warning restore CS1998
}
