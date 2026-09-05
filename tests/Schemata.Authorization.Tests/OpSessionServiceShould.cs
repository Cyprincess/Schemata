using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Services;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class OpSessionServiceShould
{
    private static ClaimsPrincipal Principal() {
        return new(new ClaimsIdentity([
            new("sub", "user-1"),
            new("sid", "sid-1"),
        ], "cookies"));
    }

    private static (Mock<IOpSessionService> Sessions, Mock<ILogoutNotifier> Notifier) Stubs() {
        var notifier = new Mock<ILogoutNotifier>();
        notifier.Setup(n => n.GetFrontChannelUrisAsync(It.IsAny<string?>(), It.IsAny<string?>(),
                                                        It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
        return (new(), notifier);
    }

    private static EndSessionHandler<SchemataApplication> CreateHandler(
        IOpSessionService                     sessions,
        ILogoutNotifier                       notifier,
        Mock<IApplicationManager<SchemataApplication>>? apps = null) {
        apps ??= new();
        var options = new SchemataAuthorizationOptions {
            Issuer = "https://as.example", SessionIdClaimType = "sid",
        };
        var sp = new Mock<IServiceProvider>();
        sp.Setup(s => s.GetService(typeof(IEnumerable<ILogoutNotifier>))).Returns(new[] { notifier });
        return new(
            apps.Object,
            TestSecurityKeys.CreateTokenService(options),
            Options.Create(options),
            sessions,
            sp.Object,
            NullLogger<EndSessionHandler<SchemataApplication>>.Instance);
    }

    [Fact]
    public async Task Invalidate_Through_The_Service_Before_Notifying_Relying_Parties() {
        var (sessions, notifier) = Stubs();
        var handler   = CreateHandler(sessions.Object, notifier.Object);
        var principal = Principal();

        await handler.HandleAsync(new(), principal, CancellationToken.None);

        sessions.Verify(s => s.InvalidateAsync(principal, "user-1", "sid-1", It.IsAny<CancellationToken>()), Times.Once);
        notifier.Verify(n => n.GetFrontChannelUrisAsync(It.IsAny<string?>(), It.IsAny<string?>(),
                                                        It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Fail_Closed_When_Session_Invalidation_Throws() {
        var (sessions, notifier) = Stubs();
        sessions.Setup(s => s.InvalidateAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<string?>(), It.IsAny<string?>(),
                                               It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("session store down"));
        var handler   = CreateHandler(sessions.Object, notifier.Object);
        var principal = Principal();

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => handler.HandleAsync(new(), principal, CancellationToken.None));

        Assert.Equal(OAuthErrors.ServerError, ex.Status);
        notifier.Verify(n => n.GetFrontChannelUrisAsync(It.IsAny<string?>(), It.IsAny<string?>(),
                                                        It.IsAny<CancellationToken>()), Times.Never);
        notifier.Verify(n => n.EnqueueBackChannelAsync(It.IsAny<string?>(), It.IsAny<string?>(),
                                                       It.IsAny<CancellationToken>()), Times.Never);
    }

}