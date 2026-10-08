using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class OpLogoutServiceShould
{
    private static ClaimsPrincipal Principal() {
        return new(new ClaimsIdentity([
            new("sub", "user-1"),
            new("sid", "sid-1"),
        ], "cookies"));
    }

    private static (
        Mock<IOpSessionService> Sessions,
        Mock<ILogoutNotifier> Notifier,
        Mock<ITokenStore<SchemataToken>> Tokens
    ) Stubs() {
        var notifier = new Mock<ILogoutNotifier>();
        notifier.Setup(n => n.PrepareAsync(It.IsAny<string?>(), It.IsAny<string?>(),
                                           It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LogoutNotificationSnapshot([], []));
        var tokens = new Mock<ITokenStore<SchemataToken>>();
        tokens.Setup(t => t.RetireParticipantsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(1);
        return (new(), notifier, tokens);
    }

    private static DefaultOpLogoutService CreateService(
        IOpSessionService         sessions,
        ILogoutNotifier           notifier,
        ITokenStore<SchemataToken> tokens
    ) {
        var sp = new ServiceCollection().AddSingleton(notifier).BuildServiceProvider();
        return new(sessions, tokens, sp, NullLogger<DefaultOpLogoutService>.Instance);
    }

    [Fact]
    public async Task Snapshot_Relying_Parties_Before_Invalidating_And_Dispatch_Then_Retire_Afterward() {
        var (sessions, notifier, tokens) = Stubs();
        var order = new List<string>();
        notifier.Setup(n => n.PrepareAsync(
                           It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Callback(() => order.Add("prepare"))
                .ReturnsAsync(new LogoutNotificationSnapshot([], []));
        sessions.Setup(s => s.InvalidateAsync(
                          It.IsAny<ClaimsPrincipal>(), It.IsAny<string?>(), It.IsAny<string?>(),
                          It.IsAny<CancellationToken>()))
                .Callback(() => order.Add("invalidate"))
                .Returns(Task.CompletedTask);
        notifier.Setup(n => n.DispatchAsync(
                           It.IsAny<LogoutNotificationSnapshot>(), It.IsAny<CancellationToken>()))
                .Callback(() => order.Add("dispatch"))
                .Returns(Task.CompletedTask);
        tokens.Setup(t => t.RetireParticipantsAsync(
                         It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
              .Callback(() => order.Add("retire"))
              .ReturnsAsync(1);
        var service = CreateService(sessions.Object, notifier.Object, tokens.Object);
        var principal = Principal();

        var result = await service.LogoutAsync(principal, "user-1", "sid-1", CancellationToken.None);

        Assert.Empty(result.FrontChannelUris);
        sessions.Verify(s => s.InvalidateAsync(principal, "user-1", "sid-1", It.IsAny<CancellationToken>()), Times.Once);
        notifier.Verify(n => n.PrepareAsync(It.IsAny<string?>(), It.IsAny<string?>(),
                                            It.IsAny<CancellationToken>()), Times.Once);
        notifier.Verify(n => n.DispatchAsync(It.IsAny<LogoutNotificationSnapshot>(),
                                             It.IsAny<CancellationToken>()), Times.Once);
        tokens.Verify(t => t.RetireParticipantsAsync("user-1", "sid-1", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(["prepare", "invalidate", "dispatch", "retire"], order);
    }

    [Fact]
    public async Task Fail_Closed_When_Session_Invalidation_Throws() {
        var (sessions, notifier, tokens) = Stubs();
        sessions.Setup(s => s.InvalidateAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<string?>(), It.IsAny<string?>(),
                                              It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("session store down"));
        var service = CreateService(sessions.Object, notifier.Object, tokens.Object);

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => service.LogoutAsync(Principal(), "user-1", "sid-1", CancellationToken.None));

        Assert.Equal(OAuthErrors.ServerError, ex.Status);
        notifier.Verify(n => n.PrepareAsync(It.IsAny<string?>(), It.IsAny<string?>(),
                                            It.IsAny<CancellationToken>()), Times.Once);
        notifier.Verify(n => n.DispatchAsync(It.IsAny<LogoutNotificationSnapshot>(),
                                             It.IsAny<CancellationToken>()), Times.Never);
        tokens.Verify(t => t.RetireParticipantsAsync(It.IsAny<string?>(), It.IsAny<string?>(),
                                                     It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Resolve_The_Browser_Session_Once_And_Target_Only_That_Session() {
        // Two sessions exist; the browser adapter supplies session A's identifier. Logging out A
        // through a principal without the SID claim must prepare, invalidate, and retire exactly
        // A — never the subject-wide null target that would also retire B's participation facts.
        var (sessions, notifier, tokens) = Stubs();
        var order = new List<string>();
        sessions.Setup(s => s.ResolveAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<string?>(),
                                           It.IsAny<CancellationToken>()))
                .Callback(() => order.Add("resolve"))
                .ReturnsAsync("sid-A");
        notifier.Setup(n => n.PrepareAsync(It.IsAny<string?>(), It.IsAny<string?>(),
                                           It.IsAny<CancellationToken>()))
                .Callback(() => order.Add("prepare"))
                .ReturnsAsync(new LogoutNotificationSnapshot([], []));
        sessions.Setup(s => s.InvalidateAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<string?>(),
                                              It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Callback(() => order.Add("invalidate"))
                .Returns(Task.CompletedTask);
        notifier.Setup(n => n.DispatchAsync(It.IsAny<LogoutNotificationSnapshot>(),
                                            It.IsAny<CancellationToken>()))
                .Callback(() => order.Add("dispatch"))
                .Returns(Task.CompletedTask);
        tokens.Setup(t => t.RetireParticipantsAsync(It.IsAny<string?>(), It.IsAny<string?>(),
                                                    It.IsAny<CancellationToken>()))
              .Callback(() => order.Add("retire"))
              .ReturnsAsync(1);
        var service = CreateService(sessions.Object, notifier.Object, tokens.Object);
        var principal = Principal();

        await service.LogoutAsync(principal, "user-1", null, CancellationToken.None);

        sessions.Verify(s => s.ResolveAsync(principal, "user-1", It.IsAny<CancellationToken>()), Times.Once);
        notifier.Verify(n => n.PrepareAsync("user-1", "sid-A", It.IsAny<CancellationToken>()), Times.Once);
        sessions.Verify(s => s.InvalidateAsync(principal, "user-1", "sid-A", It.IsAny<CancellationToken>()), Times.Once);
        tokens.Verify(t => t.RetireParticipantsAsync("user-1", "sid-A", It.IsAny<CancellationToken>()), Times.Once);
        tokens.Verify(t => t.RetireParticipantsAsync("user-1", null, It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(["resolve", "prepare", "invalidate", "dispatch", "retire"], order);
    }
}
