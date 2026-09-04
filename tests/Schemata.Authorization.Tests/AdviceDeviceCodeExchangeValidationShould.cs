using System;
using System.Threading.Tasks;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class AdviceDeviceCodeExchangeValidationShould
{
    private static readonly DateTimeOffset Now = new(2026, 8, 26, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Return_AuthorizationPending_While_Device_Code_Is_Unapproved() {
        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            Advisor().AdviseAsync(Advice(), Context(TokenStatuses.Valid)));

        Assert.Equal(OAuthErrors.AuthorizationPending, exception.Status);
    }

    [Fact]
    public async Task Return_AccessDenied_When_User_Denied_Device_Request() {
        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            Advisor().AdviseAsync(Advice(), Context(TokenStatuses.Denied)));

        Assert.Equal(OAuthErrors.AccessDenied, exception.Status);
    }

    [Fact]
    public async Task Return_ExpiredToken_When_Device_Code_Expired() {
        var context = Context(TokenStatuses.Valid);
        context.Token!.ExpireTime = Now.AddSeconds(-1).UtcDateTime;

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            Advisor().AdviseAsync(Advice(), context));

        Assert.Equal(OAuthErrors.ExpiredToken, exception.Status);
    }

    [Fact]
    public async Task Continue_When_Device_Code_Is_Authorized_For_Subject() {
        var result = await Advisor()
                           .AdviseAsync(Advice(), Context(TokenStatuses.Authorized));

        Assert.Equal(AdviseResult.Continue, result);
    }

    private static AdviceDeviceCodeExchangeValidation<SchemataApplication> Advisor() {
        var time = new Mock<TimeProvider>();
        time.Setup(value => value.GetUtcNow()).Returns(Now);
        return new(time.Object);
    }

    private static AdviceContext Advice() {
        return new(null!);
    }

    private static DeviceCodeExchangeContext<SchemataApplication> Context(string status) {
        return new() {
            Application = new() { Name = "client" },
            Token = new() {
                Type        = TokenTypes.DeviceCode,
                Status      = status,
                Application = "client",
                Parent      = "user",
                ExpireTime  = Now.AddMinutes(5).UtcDateTime,
            },
            Request = new(),
        };
    }
}
