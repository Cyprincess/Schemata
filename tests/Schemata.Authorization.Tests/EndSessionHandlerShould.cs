using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using Xunit;

namespace Schemata.Authorization.Tests;

public class EndSessionHandlerShould
{
    private static EndSessionHandler<SchemataApplication> CreateHandler(ILogoutNotifier? notifier = null) {
        var opts = new SchemataAuthorizationOptions();
        opts.AddEphemeralSigningKey();
        opts.Issuer = "https://localhost";

        var apps = new Mock<IApplicationManager<SchemataApplication>>();
        apps.Setup(a => a.ValidatePostLogoutRedirectUriAsync(It.IsAny<SchemataApplication?>(), It.IsAny<string?>(),
                                                             It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var tokenService = new TokenService(Options.Create(opts));

        var services = new ServiceCollection();
        if (notifier is not null) {
            services.AddSingleton(notifier);
        }

        var sp = services.BuildServiceProvider();

        return new(apps.Object, tokenService, Options.Create(opts), sp);
    }

    private static ClaimsPrincipal AnonymousUser() { return new(new ClaimsIdentity()); }

    [Fact]
    public async Task ReturnsRedirect_WithPostLogoutRedirectUri() {
        var handler = CreateHandler();
        var request = new EndSessionRequest { PostLogoutRedirectUri = "https://example.com/logout-callback" };

        var result = await handler.HandleAsync(request, AnonymousUser(), CancellationToken.None);

        Assert.Equal(AuthorizationStatus.Redirect, result.Status);
        Assert.Equal("https://example.com/logout-callback", result.RedirectUri);
    }

    [Fact]
    public async Task ReturnsRedirect_WithStateAppended() {
        var handler = CreateHandler();
        var request = new EndSessionRequest {
            PostLogoutRedirectUri = "https://example.com/logout-callback", State = "xyz",
        };

        var result = await handler.HandleAsync(request, AnonymousUser(), CancellationToken.None);

        Assert.Equal(AuthorizationStatus.Redirect, result.Status);
        Assert.Contains("state=xyz", result.RedirectUri);
    }

    [Fact]
    public async Task ReturnsEmpty_WhenNoRedirectUri() {
        var handler = CreateHandler();
        var request = new EndSessionRequest();

        var result = await handler.HandleAsync(request, AnonymousUser(), CancellationToken.None);

        Assert.Equal(AuthorizationStatus.Content, result.Status);
    }

    [Fact]
    public async Task ReturnsEmpty_WhenRedirectUriIsWhitespace() {
        var handler = CreateHandler();
        var request = new EndSessionRequest { PostLogoutRedirectUri = "   " };

        var result = await handler.HandleAsync(request, AnonymousUser(), CancellationToken.None);

        Assert.Equal(AuthorizationStatus.Content, result.Status);
    }

    [Fact]
    public async Task AppendStateWithAmpersand_WhenRedirectAlreadyHasQuery() {
        var handler = CreateHandler();
        var request = new EndSessionRequest {
            PostLogoutRedirectUri = "https://example.com/callback?foo=bar", State = "s1",
        };

        var result = await handler.HandleAsync(request, AnonymousUser(), CancellationToken.None);

        Assert.Equal(AuthorizationStatus.Redirect, result.Status);
        Assert.Equal("https://example.com/callback?foo=bar&state=s1", result.RedirectUri);
    }

    [Fact]
    public async Task ReturnsHtml_WhenFrontChannelUrisExist() {
        var notifier = new Mock<ILogoutNotifier>();
        notifier
           .Setup(n => n.GetFrontChannelUrisAsync(It.IsAny<string?>(), It.IsAny<string?>(),
                                                  It.IsAny<CancellationToken>()))
           .ReturnsAsync(["https://rp1.example.com/logout", "https://rp2.example.com/logout"]);

        var handler = CreateHandler(notifier.Object);
        var request = new EndSessionRequest { PostLogoutRedirectUri = "https://example.com/done" };

        var user   = new ClaimsPrincipal(new ClaimsIdentity([new("sub", "user-1")], "test"));
        var result = await handler.HandleAsync(request, user, CancellationToken.None);

        Assert.Equal(AuthorizationStatus.Content, result.Status);
        var html = Assert.IsType<string>(result.Data);

        Assert.Contains("rp1.example.com/logout", html);
        Assert.Contains("rp2.example.com/logout", html);
        Assert.Contains("<iframe", html);
        Assert.Contains("meta http-equiv=\"refresh\"", html);
        Assert.Contains("example.com/done", html);
    }

    [Fact]
    public void BuildLogoutPage_NoJs_HasMetaRefresh() {
        var uris = new List<string> { "https://rp.example.com/logout" };
        var html = EndSessionHandler<SchemataApplication>.BuildLogoutPage(uris, "https://example.com/done");

        Assert.Contains("<meta http-equiv=\"refresh\" content=\"5;url=", html);
        Assert.Contains("<a href=", html);
        Assert.Contains("<iframe", html);
    }

    [Fact]
    public void BuildLogoutPage_NoRedirect_NoMetaRefresh() {
        var uris = new List<string> { "https://rp.example.com/logout" };
        var html = EndSessionHandler<SchemataApplication>.BuildLogoutPage(uris, null);

        Assert.DoesNotContain("meta http-equiv", html);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("<iframe", html);
    }
}
