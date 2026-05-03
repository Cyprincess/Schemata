using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Models;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Tests;

public class TokenHandlerShould
{
    private static TokenHandler CreateHandler(params (string grantType, Mock<IGrantHandler> mock)[] grants) {
        var services = new ServiceCollection();
        foreach (var (grantType, mock) in grants) {
            services.AddKeyedScoped<IGrantHandler>(grantType, (_, _) => mock.Object);
        }

        return new(services.BuildServiceProvider());
    }

    private static Mock<IGrantHandler> MockGrant(string grantType) {
        var mock = new Mock<IGrantHandler>(MockBehavior.Strict);
        mock.Setup(h => h.GrantType).Returns(grantType);
        return mock;
    }

    [Fact]
    public async Task RoutesToMatchingGrantHandler() {
        var grant = MockGrant(GrantTypes.ClientCredentials);
        grant.Setup(h => h.HandleAsync(It.IsAny<TokenRequest>(), It.IsAny<Dictionary<string, List<string?>>?>(),
                                       It.IsAny<CancellationToken>()))
             .ReturnsAsync(AuthorizationResult.Content(new { }));

        var handler = CreateHandler((GrantTypes.ClientCredentials, grant));
        var request = new TokenRequest { GrantType = GrantTypes.ClientCredentials };

        await handler.HandleAsync(request, null, CancellationToken.None);

        grant.Verify(h => h.HandleAsync(request, null, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ThrowsUnsupportedGrantType_WhenNoMatchingHandler() {
        var grant   = MockGrant(GrantTypes.ClientCredentials);
        var handler = CreateHandler((GrantTypes.ClientCredentials, grant));
        var request = new TokenRequest { GrantType = "unknown" };

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.HandleAsync(
                                                              request, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.UnsupportedGrantType, ex.Code);
    }

    [Fact]
    public async Task ThrowsUnsupportedGrantType_WhenGrantTypeNull() {
        var grant   = MockGrant(GrantTypes.ClientCredentials);
        var handler = CreateHandler((GrantTypes.ClientCredentials, grant));
        var request = new TokenRequest { GrantType = null };

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.HandleAsync(
                                                              request, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.UnsupportedGrantType, ex.Code);
    }

    [Fact]
    public async Task ReturnsResultFromMatchedHandler() {
        var expected = AuthorizationResult.Content(new { token = "abc" });
        var grant    = MockGrant(GrantTypes.RefreshToken);
        grant.Setup(h => h.HandleAsync(It.IsAny<TokenRequest>(), It.IsAny<Dictionary<string, List<string?>>?>(),
                                       It.IsAny<CancellationToken>()))
             .ReturnsAsync(expected);

        var handler = CreateHandler((GrantTypes.RefreshToken, grant));
        var request = new TokenRequest { GrantType = GrantTypes.RefreshToken };

        var result = await handler.HandleAsync(request, null, CancellationToken.None);

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task RoutesToCorrectHandler_WhenMultipleRegistered() {
        var cc      = MockGrant(GrantTypes.ClientCredentials);
        var refresh = MockGrant(GrantTypes.RefreshToken);

        refresh.Setup(h => h.HandleAsync(It.IsAny<TokenRequest>(), It.IsAny<Dictionary<string, List<string?>>?>(),
                                         It.IsAny<CancellationToken>()))
               .ReturnsAsync(AuthorizationResult.Content(new { }));

        var handler = CreateHandler((GrantTypes.ClientCredentials, cc), (GrantTypes.RefreshToken, refresh));
        var request = new TokenRequest { GrantType = GrantTypes.RefreshToken };

        await handler.HandleAsync(request, null, CancellationToken.None);

        refresh.Verify(h => h.HandleAsync(request, null, CancellationToken.None), Times.Once);
        cc.Verify(
            h => h.HandleAsync(It.IsAny<TokenRequest>(), It.IsAny<Dictionary<string, List<string?>>?>(),
                               It.IsAny<CancellationToken>()), Times.Never);
    }
}
