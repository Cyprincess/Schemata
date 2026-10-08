using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Identity.Foundation.Controllers;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Entities;
using Xunit;

namespace Schemata.Identity.Tests;

public class AuthenticateSignOutShould
{
    [Fact]
    public async Task Run_Observers_Before_Clearing_Either_Scheme() {
        var order = new List<string>();
        var observer = new Mock<IHostSignInObserver>();
        observer.Setup(value => value.OnSigningOutAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
                .Callback(() => order.Add("observer"))
                .ReturnsAsync((HostSignOutResponse?)null);
        var authentication = Authentication(order);
        var principal = new ClaimsPrincipal(new ClaimsIdentity("test"));
        var controller = Controller(authentication.Object, principal, observer.Object);

        await Assert.ThrowsAsync<NoContentException>(() => controller.SignOut(CancellationToken.None));

        observer.Verify(value => value.OnSigningOutAsync(principal, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal([
            "observer",
            $"signout:{IdentityConstants.ApplicationScheme}",
            $"signout:{IdentityConstants.BearerScheme}",
        ], order);
    }

    [Fact]
    public async Task Abort_Signout_When_An_Observer_Fails() {
        var observer = new Mock<IHostSignInObserver>();
        observer.Setup(value => value.OnSigningOutAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("bridge down"));
        var authentication = Authentication(new());
        var controller = Controller(
            authentication.Object, new(new ClaimsIdentity("test")), observer.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.SignOut(CancellationToken.None));

        authentication.Verify(value => value.SignOutAsync(
            It.IsAny<HttpContext>(), It.IsAny<string?>(),
            It.IsAny<AuthenticationProperties?>()), Times.Never);
    }

    [Fact]
    public async Task Render_A_Single_Observer_Response_After_Clearing_Both_Schemes() {
        var order = new List<string>();
        var observer = new Mock<IHostSignInObserver>();
        observer.Setup(value => value.OnSigningOutAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HostSignOutResponse("<html>logout</html>", "text/html"));
        var authentication = Authentication(order);
        var controller = Controller(
            authentication.Object, new(new ClaimsIdentity("test")), observer.Object);

        var result = await controller.SignOut(CancellationToken.None);

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal("<html>logout</html>", content.Content);
        Assert.Equal("text/html", content.ContentType);
        Assert.Equal([
            $"signout:{IdentityConstants.ApplicationScheme}",
            $"signout:{IdentityConstants.BearerScheme}",
        ], order);
    }

    [Fact]
    public async Task Fail_When_Two_Observers_Supply_Conflicting_Responses() {
        var first = new Mock<IHostSignInObserver>();
        first.Setup(value => value.OnSigningOutAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new HostSignOutResponse("<html>first</html>", "text/html"));
        var second = new Mock<IHostSignInObserver>();
        second.Setup(value => value.OnSigningOutAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new HostSignOutResponse("<html>second</html>", "text/html"));
        var authentication = Authentication(new());
        var controller = Controller(
            authentication.Object, new(new ClaimsIdentity("test")),
            first.Object, second.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.SignOut(CancellationToken.None));

        authentication.Verify(value => value.SignOutAsync(
            It.IsAny<HttpContext>(), It.IsAny<string?>(),
            It.IsAny<AuthenticationProperties?>()), Times.Never);
    }

    [Fact]
    public async Task Sign_Out_Standalone_When_No_Observers_Are_Registered() {
        var order = new List<string>();
        var authentication = Authentication(order);
        var controller = Controller(authentication.Object, new(new ClaimsIdentity("test")));

        await Assert.ThrowsAsync<NoContentException>(() => controller.SignOut(CancellationToken.None));

        Assert.Equal([
            $"signout:{IdentityConstants.ApplicationScheme}",
            $"signout:{IdentityConstants.BearerScheme}",
        ], order);
    }

    private static Mock<IAuthenticationService> Authentication(List<string> order) {
        var authentication = new Mock<IAuthenticationService>(MockBehavior.Strict);
        authentication.Setup(value => value.SignOutAsync(
                          It.IsAny<HttpContext>(), It.IsAny<string?>(),
                          It.IsAny<AuthenticationProperties?>()))
                      .Callback((HttpContext _, string? scheme, AuthenticationProperties? _) =>
                           order.Add($"signout:{scheme}"))
                      .Returns(Task.CompletedTask);
        return authentication;
    }

    private static AuthenticateController<SchemataUser> Controller(
        IAuthenticationService               authentication,
        ClaimsPrincipal                      principal,
        params IHostSignInObserver[]         observers
    ) {
        var http = new DefaultHttpContext {
            User            = principal,
            RequestServices = new ServiceCollection().AddSingleton(authentication).BuildServiceProvider(),
        };
        return new(null!, null!, observers) {
            ControllerContext = new() { HttpContext = http },
        };
    }
}
