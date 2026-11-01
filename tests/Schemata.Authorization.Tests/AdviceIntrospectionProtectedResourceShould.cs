using System.Collections.Generic;
using System.Linq;
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
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Security.Skeleton.Entities;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

/// <summary>
///     The RFC 7662 §2 resource-applicability matrix of
///     <see cref="AdviceIntrospectionProtectedResource{TApp}" />: an access token reports active
///     only to a caller whose configured resource mapping covers one of its verified audiences.
/// </summary>
public class AdviceIntrospectionProtectedResourceShould
{
    private const string Caller   = "introspect-client";
    private const string Resource = "https://api.example.com/";

    [Fact]
    public async Task Continue_When_An_Access_Token_Audience_Matches_The_Caller_Mapping() {
        var (advisor, _) = Create([Resource]);

        var result = await advisor.AdviseAsync(Ctx(), Context(TokenTypes.AccessToken, [Resource]));

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task Block_When_No_Access_Token_Audience_Matches_The_Caller_Mapping() {
        var (advisor, _) = Create([Resource]);

        var result = await advisor.AdviseAsync(Ctx(), Context(TokenTypes.AccessToken, ["https://other.example.com/"]));

        Assert.Equal(AdviseResult.Block, result);
    }

    [Fact]
    public async Task Block_When_The_Audience_Differs_Only_By_Case() {
        var (advisor, _) = Create([Resource]);

        var result = await advisor.AdviseAsync(Ctx(), Context(TokenTypes.AccessToken, [Resource.ToUpperInvariant()]));

        Assert.Equal(AdviseResult.Block, result);
    }

    [Fact]
    public async Task Block_When_The_Caller_Has_No_Mapping() {
        var (advisor, _) = Create(null);

        var result = await advisor.AdviseAsync(Ctx(), Context(TokenTypes.AccessToken, [Resource]));

        Assert.Equal(AdviseResult.Block, result);
    }

    [Fact]
    public async Task Block_When_The_Caller_Mapping_Is_Empty() {
        var (advisor, _) = Create([]);

        var result = await advisor.AdviseAsync(Ctx(), Context(TokenTypes.AccessToken, [Resource]));

        Assert.Equal(AdviseResult.Block, result);
    }

    [Fact]
    public async Task Block_When_The_Caller_Client_Id_Is_Blank() {
        var (advisor, _) = Create([Resource]);

        var result = await advisor.AdviseAsync(Ctx(), Context(TokenTypes.AccessToken, [Resource], callerClientId: " "));

        Assert.Equal(AdviseResult.Block, result);
    }

    [Fact]
    public async Task Block_When_The_Token_Principal_Carries_No_Audience() {
        var (advisor, _) = Create([Resource]);

        var result = await advisor.AdviseAsync(Ctx(), Context(TokenTypes.AccessToken, []));

        Assert.Equal(AdviseResult.Block, result);
    }

    [Fact]
    public async Task Continue_For_Refresh_Tokens_Without_Any_Mapping() {
        var (advisor, _) = Create(null);

        var result = await advisor.AdviseAsync(Ctx(), Context(TokenTypes.RefreshToken, []));

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task Throw_InvalidClient_For_Public_Clients() {
        var (advisor, _) = Create([Resource]);
        var context = Context(TokenTypes.AccessToken, [Resource]);
        context.Application!.TokenEndpointAuthMethod = ClientAuthMethods.None;
        context.Application.ApplicationType          = ApplicationTypes.Native;

        var ex = await Assert.ThrowsAsync<OAuthException>(() => advisor.AdviseAsync(Ctx(), context));

        Assert.Equal(OAuthErrors.InvalidClient, ex.Status);
        Assert.Equal(401, ex.Code);
    }

    [Fact]
    public async Task Throw_UnauthorizedClient_Without_The_Endpoint_Permission() {
        var (advisor, manager) = Create([Resource]);
        manager.Setup(m => m.HasPermissionAsync(
                   It.IsAny<SchemataApplication?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(false);

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => advisor.AdviseAsync(Ctx(), Context(TokenTypes.AccessToken, [Resource])));

        Assert.Equal(OAuthErrors.UnauthorizedClient, ex.Status);
        Assert.Equal(403, ex.Code);
    }

    private static (AdviceIntrospectionProtectedResource<SchemataApplication> Advisor, Mock<IApplicationManager<SchemataApplication>> Manager)
        Create(List<string>? audiences) {
        var options = new SchemataAuthorizationOptions();
        if (audiences is not null) {
            options.IntrospectionResourceAudiences[Caller] = audiences;
        }

        var manager = new Mock<IApplicationManager<SchemataApplication>>();
        manager.Setup(m => m.HasPermissionAsync(
                   It.IsAny<SchemataApplication?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(true);

        return (new(manager.Object, Options.Create(options)), manager);
    }

    private static AdviceContext Ctx() {
        return new(new ServiceCollection().BuildServiceProvider());
    }

    private static IntrospectionContext<SchemataApplication> Context(
        string   tokenType,
        string[] audiences,
        string?  callerClientId = Caller
    ) {
        return new() {
            Application = new() {
                ClientId                = callerClientId,
                TokenEndpointAuthMethod = ClientAuthMethods.ClientSecretPost,
            },
            Token     = new() { Type = tokenType, Status = TokenStatuses.Valid },
            Principal = new(new ClaimsIdentity(audiences.Select(a => new Claim(Claims.Audience, a)))),
        };
    }
}
