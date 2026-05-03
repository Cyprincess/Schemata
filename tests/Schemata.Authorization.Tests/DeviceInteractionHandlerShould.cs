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
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Tests;

public class DeviceInteractionHandlerShould
{
    private static Fixture CreateFixture() {
        var jsonOpts = Options.Create(new JsonSerializerOptions());
        var authOpts = Options.Create(new SchemataAuthorizationOptions { SessionIdClaimType = "sid" });

        var app  = new SchemataApplication { Id = 1, ClientId = "device-client" };
        var apps = new Mock<IApplicationManager<SchemataApplication>>();
        apps.Setup(a => a.FindByCanonicalNameAsync("device-client", It.IsAny<CancellationToken>())).ReturnsAsync(app);

        var devicePayload = JsonSerializer.Serialize(
            new DeviceCodePayload { Scope = "openid profile", ClientId = "device-client" },
            jsonOpts.Value
        );

        var device = new SchemataToken {
            Id          = 2,
            Name        = "device-1",
            Type        = TokenTypes.DeviceCode,
            Status      = TokenStatuses.Valid,
            ReferenceId = "dev-ref",
            Payload     = devicePayload,
            ExpireTime  = DateTime.UtcNow.AddMinutes(10),
        };

        var userCodePayload = JsonSerializer.Serialize(
            new UserCodePayload {
                DeviceCodeName = device.Name, Scope = "openid profile", ClientId = "device-client",
            },
            jsonOpts.Value
        );

        var userCode = new SchemataToken {
            Id          = 1,
            Name        = "user-1",
            Type        = TokenTypes.UserCode,
            Status      = TokenStatuses.Valid,
            ReferenceId = "user-ref",
            Payload     = userCodePayload,
            ExpireTime  = DateTime.UtcNow.AddMinutes(10),
        };

        var tokens = new Mock<ITokenManager<SchemataToken>>();
        tokens.Setup(t => t.FindByReferenceIdAsync("user-ref", It.IsAny<CancellationToken>())).ReturnsAsync(userCode);
        tokens.Setup(t => t.FindByCanonicalNameAsync(device.Name!, It.IsAny<CancellationToken>())).ReturnsAsync(device);

        var scopes   = new Mock<IScopeManager<SchemataScope>>();
        var authzMgr = new Mock<IAuthorizationManager<SchemataAuthorization>>();
        authzMgr.Setup(m => m.CreateAsync(It.IsAny<SchemataAuthorization>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((SchemataAuthorization a, CancellationToken _) => {
                         a.Name = "auth-generated";
                         return a;
                     }
                 );

        var handler
            = new DeviceInteractionHandler<SchemataApplication, SchemataAuthorization, SchemataScope, SchemataToken>(
                apps.Object,
                tokens.Object,
                scopes.Object,
                authzMgr.Object,
                authOpts,
                jsonOpts
            );

        return new(handler, tokens, authzMgr, device, userCode);
    }

    private static ClaimsPrincipal CreatePrincipal(string subject = "user-42", string sid = "sess-99") {
        return new(new ClaimsIdentity([new(Claims.Subject, subject), new("sid", sid)], "test"));
    }

    [Fact]
    public async Task CreatesAuthorization_AndBindsToDeviceToken_OnApprove() {
        var f         = CreateFixture();
        var principal = CreatePrincipal(sid: "sess-xyz");

        await Assert.ThrowsAsync<NoContentException>(() => f.Handler.ApproveAsync(
                                                         new() { Code = "user-ref" },
                                                         principal,
                                                         "https://auth",
                                                         CancellationToken.None
                                                     )
        );

        var createInvocation = Assert.Single(
            f.AuthzMgr.Invocations,
            i => i.Method.Name == nameof(IAuthorizationManager<SchemataAuthorization>.CreateAsync)
        );
        var created = Assert.IsType<SchemataAuthorization>(createInvocation.Arguments[0]);
        Assert.Equal("device-client", created.ApplicationName);
        Assert.Equal("user-42", created.Subject);
        Assert.Equal("openid profile", created.Scopes);
        Assert.Equal(TokenStatuses.Valid, created.Status);

        Assert.Equal("auth-generated", f.Device.AuthorizationName);
        Assert.Equal("sess-xyz", f.Device.SessionId);
        Assert.Equal("user-42", f.Device.Subject);
        Assert.Equal(TokenStatuses.Authorized, f.Device.Status);
    }

    [Fact]
    public async Task DoesNotCreateAuthorization_WhenSubjectMissing() {
        var f         = CreateFixture();
        var principal = new ClaimsPrincipal(new ClaimsIdentity("test"));

        await Assert.ThrowsAsync<OAuthException>(() => f.Handler.ApproveAsync(
                                                     new() { Code = "user-ref" },
                                                     principal,
                                                     "https://auth",
                                                     CancellationToken.None
                                                 )
        );

        Assert.DoesNotContain(
            f.AuthzMgr.Invocations,
            i => i.Method.Name == nameof(IAuthorizationManager<SchemataAuthorization>.CreateAsync)
        );
    }

    #region Nested type: Fixture

    private record Fixture(
        DeviceInteractionHandler<SchemataApplication, SchemataAuthorization, SchemataScope, SchemataToken> Handler,
        Mock<ITokenManager<SchemataToken>>                                                                 Tokens,
        Mock<IAuthorizationManager<SchemataAuthorization>>                                                 AuthzMgr,
        SchemataToken                                                                                      Device,
        SchemataToken                                                                                      UserCode
    );

    #endregion
}
