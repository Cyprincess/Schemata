using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Tests;

public class AdviceAuthorizeConsentShould
{
    [Fact]
    public async Task GrantConsent_WhenExistingAuthorizationCoversRequestedScopes() {
        var authorization = new SchemataAuthorization {
            Status = TokenStatuses.Valid, Type = AuthorizationTypes.Permanent, Scopes = "openid profile email",
        };
        var authzMgr = SetupAuthzMgr(authorization);

        var advisor = new AdviceAuthorizeConsent<SchemataApplication, SchemataAuthorization>(authzMgr.Object);
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var authz = new AuthorizeContext<SchemataApplication> {
            Application = new() { ClientId  = "test-app", ConsentType = ConsentTypes.Explicit },
            Request     = new() { Scope = "openid profile" },
            Principal   = CreatePrincipal("user-1"),
        };

        await advisor.AdviseAsync(ctx, authz);

        Assert.Equal(ConsentDecision.Granted, authz.ConsentDecision);
    }

    [Fact]
    public async Task NotGrantConsent_WhenRequestedScopesExceedGranted() {
        var authorization = new SchemataAuthorization { Status = TokenStatuses.Valid, Scopes = "openid profile" };
        var authzMgr      = SetupAuthzMgr(authorization);

        var advisor = new AdviceAuthorizeConsent<SchemataApplication, SchemataAuthorization>(authzMgr.Object);
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var authz = new AuthorizeContext<SchemataApplication> {
            Application = new() { ClientId = "test-app", ConsentType = ConsentTypes.Explicit },
            Request     = new() { Scope    = "openid profile email" },
            Principal   = CreatePrincipal("user-1"),
        };

        await advisor.AdviseAsync(ctx, authz);

        Assert.NotEqual(ConsentDecision.Granted, authz.ConsentDecision);
    }

    private static ClaimsPrincipal CreatePrincipal(string subject) {
        return new(new ClaimsIdentity([new(Claims.Subject, subject)], "test"));
    }

    private static Mock<IAuthorizationManager<SchemataAuthorization>> SetupAuthzMgr(
        SchemataAuthorization authorization
    ) {
        var mock = new Mock<IAuthorizationManager<SchemataAuthorization>>();
        mock.Setup(m => m.ListAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(ToAsync(authorization));
        return mock;
    }

#pragma warning disable CS1998
    private static async IAsyncEnumerable<T> ToAsync<T>(T item) { yield return item; }
#pragma warning restore CS1998
}
