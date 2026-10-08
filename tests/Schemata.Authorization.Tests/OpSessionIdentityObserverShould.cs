using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Identity.Foundation.Controllers;
using Schemata.Identity.Skeleton.Entities;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Tests;
public class OpSessionIdentityObserverShould
{
    private static (
        OpSessionIdentityObserver Observer,
        Mock<IOpLogoutService>    Logout
    ) Create() {
        var logout = new Mock<IOpLogoutService>();
        var observer = new OpSessionIdentityObserver(
            new Mock<IOpSessionService>().Object,
            logout.Object,
            Options.Create(new SchemataAuthorizationOptions { SessionIdClaimType = "sid" }));
        return (observer, logout);
    }

    [Fact]
    public async Task Bridge_Host_Signout_Into_The_Op_Logout_Orchestrator() {
        var (observer, logout) = Create();
        logout.Setup(value => value.LogoutAsync(
                         It.IsAny<ClaimsPrincipal?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                         It.IsAny<CancellationToken>()))
              .ReturnsAsync(new OpLogoutResult([]));
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(IdentityClaims.Subject, "users/u-1"),
            new("sid",                  "sid-1"),
        ], "cookies"));

        var response = await observer.OnSigningOutAsync(principal, CancellationToken.None);

        Assert.Null(response);
        logout.Verify(value => value.LogoutAsync(
            principal, "users/u-1", "sid-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Sign_Out_Through_The_Subject_Alone_When_No_Session_Claim_Is_Present() {
        var (observer, logout) = Create();
        logout.Setup(value => value.LogoutAsync(
                         It.IsAny<ClaimsPrincipal?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                         It.IsAny<CancellationToken>()))
              .ReturnsAsync(new OpLogoutResult([]));
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(IdentityClaims.Subject, "users/u-1"),
        ], "cookies"));

        var response = await observer.OnSigningOutAsync(principal, CancellationToken.None);

        Assert.Null(response);
        logout.Verify(value => value.LogoutAsync(
            principal, "users/u-1", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Do_Nothing_When_The_Principal_Carries_No_Session_Evidence() {
        var (observer, logout) = Create();
        var principal = new ClaimsPrincipal(new ClaimsIdentity("cookies"));

        await observer.OnSigningOutAsync(principal, CancellationToken.None);

        logout.Verify(value => value.LogoutAsync(
            It.IsAny<ClaimsPrincipal?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Propagate_An_Orchestrator_Failure_To_The_Host_Signout() {
        var (observer, logout) = Create();
        logout.Setup(value => value.LogoutAsync(
                         It.IsAny<ClaimsPrincipal?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                         It.IsAny<CancellationToken>()))
              .ThrowsAsync(new InvalidOperationException("session store down"));
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(IdentityClaims.Subject, "users/u-1"),
            new("sid",                  "sid-1"),
        ], "cookies"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => observer.OnSigningOutAsync(principal, CancellationToken.None));
    }

    [Fact]
    public async Task Render_The_Front_Channel_Logout_Page_Through_The_Host_Signout() {
        var logout = new Mock<IOpLogoutService>();
        logout.Setup(value => value.LogoutAsync(
                         It.IsAny<ClaimsPrincipal?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                         It.IsAny<CancellationToken>()))
              .ReturnsAsync(new OpLogoutResult(["https://rp.example/logout"]));
        var observer = new OpSessionIdentityObserver(
            new Mock<IOpSessionService>().Object,
            logout.Object,
            Options.Create(new SchemataAuthorizationOptions { SessionIdClaimType = "sid" }));
        var signouts = new List<string?>();
        var authentication = new Mock<IAuthenticationService>(MockBehavior.Strict);
        authentication.Setup(value => value.SignOutAsync(
                          It.IsAny<HttpContext>(), It.IsAny<string?>(),
                          It.IsAny<AuthenticationProperties?>()))
                      .Callback((HttpContext _, string? scheme, AuthenticationProperties? _) =>
                           signouts.Add(scheme))
                      .Returns(Task.CompletedTask);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(IdentityClaims.Subject, "users/u-1"),
            new("sid",                  "sid-1"),
        ], "cookies"));
        var http = new DefaultHttpContext {
            User            = principal,
            RequestServices = new ServiceCollection().AddSingleton(authentication.Object).BuildServiceProvider(),
        };
        var controller = new AuthenticateController<SchemataUser>(null!, null!, [observer]) {
            ControllerContext = new() { HttpContext = http },
        };

        var result = await controller.SignOut(CancellationToken.None);

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal("text/html; charset=utf-8", content.ContentType);
        Assert.Contains("<iframe src=\"https://rp.example/logout\"", content.Content);
        Assert.Equal(2, signouts.Count);
    }
}
