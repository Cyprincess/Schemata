using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class CodeReplayCascadeShould
{
    private static readonly DateTimeOffset Now = new(2026, 8, 26, 0, 0, 0, TimeSpan.Zero);

    private static AdviceContext Ctx() {
        return new(new ServiceCollection().BuildServiceProvider());
    }

    [Fact]
    public async Task Revoke_Derived_Tokens_When_A_Redeemed_Code_Is_Presented_Again() {
        var tokens   = new Mock<ITokenStore<SchemataToken>>();
        var advisor  = new AdviceCodeExchangeValidation<SchemataApplication>(tokens.Object, new FakeTimeProvider(Now));
        var exchange = ValidExchange(TokenStatuses.Redeemed);

        var ex = await Assert.ThrowsAsync<OAuthException>(() => advisor.AdviseAsync(Ctx(), exchange));

        Assert.Equal(OAuthErrors.InvalidGrant, ex.Status);
        tokens.Verify(t => t.RevokeByAuthorizationAsync("authorizations/grant-1", It.IsAny<CancellationToken>()),
                      Times.Once);
    }

    [Fact]
    public async Task Not_Cascade_When_The_Code_Was_Revoked_Rather_Than_Redeemed() {
        var tokens  = new Mock<ITokenStore<SchemataToken>>();
        var advisor = new AdviceCodeExchangeValidation<SchemataApplication>(tokens.Object, new FakeTimeProvider(Now));

        await Assert.ThrowsAsync<OAuthException>(() => advisor.AdviseAsync(Ctx(), ValidExchange(TokenStatuses.Revoked)));

        tokens.Verify(t => t.RevokeByAuthorizationAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()),
                      Times.Never);
    }

    private static CodeExchangeContext<SchemataApplication> ValidExchange(string status) {
        return new() {
            Application = new() { ClientId = "client-1", CanonicalName = "applications/client-1" },
            CodeToken   = new() {
                Type = TokenTypes.AuthorizationCode, Status = status, Application = "applications/client-1",
                Authorization = "authorizations/grant-1", ExpireTime = Now.AddMinutes(5).UtcDateTime,
            },
            Payload = new() { ClientId = "client-1", RedirectUri = "https://rp/cb" },
            Request = new() {
                GrantType = GrantTypes.AuthorizationCode, RedirectUri = "https://rp/cb",
            },
        };
    }
}