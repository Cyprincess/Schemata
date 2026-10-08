using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class UserInfoHandlerShould
{
    [Fact]
    public async Task Return_DestinationApproved_Subject_Client_And_Profile_Claims() {
        var claimsAdvisor = new Mock<IClaimsAdvisor>();
        claimsAdvisor.SetupGet(value => value.Order).Returns(0);
        claimsAdvisor.Setup(value => value.AdviseAsync(
                                It.IsAny<AdviceContext>(),
                                It.IsAny<List<Claim>>(),
                                It.IsAny<AuthorizationClaimContext>(),
                                It.IsAny<CancellationToken>()))
                     .Callback((AdviceContext _, List<Claim> claims, AuthorizationClaimContext _, CancellationToken _) => {
                         claims.Add(new(IdentityClaims.Email, "alice@example.com"));
                         claims.Add(new("internal", "secret"));
                         claims.Add(new("email_verified", "true", ClaimValueTypes.Boolean));
                         claims.Add(new("age", "42", ClaimValueTypes.Integer32));
                         claims.Add(new("address", "{\"country\":\"NZ\"}", "JSON"));
                     })
                     .ReturnsAsync(AdviseResult.Continue);
        var destinationAdvisor = new Mock<IDestinationAdvisor>();
        destinationAdvisor.SetupGet(value => value.Order).Returns(0);
        destinationAdvisor.Setup(value => value.AdviseAsync(
                                     It.IsAny<AdviceContext>(),
                                     It.IsAny<Claim>(),
                                     It.IsAny<HashSet<string>>(),
                                     It.IsAny<ClaimsPrincipal>(),
                                     It.IsAny<AuthorizationClaimContext>(),
                                     It.IsAny<CancellationToken>()))
                          .Callback((AdviceContext _, Claim claim, HashSet<string> destinations,
                                     ClaimsPrincipal _, AuthorizationClaimContext _, CancellationToken _) => {
                              if (claim.Type != "internal") {
                                  destinations.Add(ClaimDestinations.UserInfo);
                              }
                          })
                          .ReturnsAsync(AdviseResult.Continue);
        var services = new ServiceCollection();
        services.AddSingleton(claimsAdvisor.Object);
        services.AddSingleton(destinationAdvisor.Object);
        services.AddSingleton<IUserInfoAdvisor>(new AdviceUserInfoEndUserRequirement());
        using var provider = services.BuildServiceProvider();
        using var ambient  = AdviceContext.Establish(new(provider));
        var       handler  = new UserInfoHandler(protector: null);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(IdentityClaims.Subject, "user-1"),
            new(Claims.ClientId, "client-1"),
            new(Claims.Scope, "profile email"),
            new(Claims.GrantSubjectKind, GrantSubjectKinds.EndUser),
        ], "bearer"));

        var result = await handler.HandleAsync(principal, CancellationToken.None);

        Assert.Equal(AuthorizationStatus.Content, result.Status);
        var content = Assert.IsType<Dictionary<string, object>>(result.Data);
        Assert.Equal("user-1", content[IdentityClaims.Subject]);
        Assert.Equal("client-1", content[Claims.ClientId]);
        Assert.Equal("alice@example.com", content[IdentityClaims.Email]);
        Assert.DoesNotContain("internal", content);
        using var wire = JsonDocument.Parse(JsonSerializer.Serialize(content));
        Assert.True(wire.RootElement.GetProperty("email_verified").GetBoolean());
        Assert.Equal(42, wire.RootElement.GetProperty("age").GetInt32());
        Assert.Equal("NZ", wire.RootElement.GetProperty("address").GetProperty("country").GetString());
    }

    [Fact]
    public async Task Reject_An_Application_Subject_Even_When_It_Has_Profile_Scopes() {
        var services = new ServiceCollection();
        services.AddSingleton<IUserInfoAdvisor>(new AdviceUserInfoEndUserRequirement());
        using var provider = services.BuildServiceProvider();
        using var ambient  = AdviceContext.Establish(new(provider));
        var handler = new UserInfoHandler(protector: null);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(IdentityClaims.Subject, "applications/client-1"),
            new(Claims.ClientId, "client-1"),
            new(Claims.Scope, "profile email"),
            new(Claims.GrantSubjectKind, GrantSubjectKinds.Application),
        ], "bearer"));

        var exception = await Assert.ThrowsAsync<Abstractions.Exceptions.OAuthException>(
            () => handler.HandleAsync(principal, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidRequest, exception.Status);
    }
}
