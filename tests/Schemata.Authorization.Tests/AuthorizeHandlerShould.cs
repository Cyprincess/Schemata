using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Tests;

public class AuthorizeHandlerShould
{
    private static AuthorizeHandler<SchemataApplication, SchemataToken> CreateHandler() {
        var opts = new SchemataAuthorizationOptions();
        opts.AddEphemeralSigningKey();
        opts.Issuer         = "https://localhost";
        opts.InteractionUri = "https://localhost/consent";

        var tokens = new Mock<ITokenManager<SchemataToken>>();
        tokens.Setup(t => t.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((SchemataToken t, CancellationToken _) => t);

        var tokenService = new TokenService(Options.Create(opts));
        var jsonOpts     = Options.Create(new JsonSerializerOptions());

        var services = new ServiceCollection();
        var sp       = services.BuildServiceProvider();

        return new(tokens.Object, tokenService, Options.Create(opts), sp, jsonOpts);
    }

    private static ClaimsPrincipal AuthenticatedUser(string subject = "user-1") {
        return new(new ClaimsIdentity([new(Claims.Subject, subject)], "test"));
    }

    [Fact]
    public async Task ThrowInvalidClient_WhenApplicationMissing() {
        // No advisors registered => pipeline returns Continue, but SchemataApplication is never set in context.
        var handler = CreateHandler();
        var request = new AuthorizeRequest { ClientId = "test", ResponseType = "code" };

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.AuthorizeAsync(
                                                              request,
                                                              AuthenticatedUser(),
                                                              CancellationToken.None
                                                          )
        );

        Assert.Equal(OAuthErrors.InvalidClient, ex.Code);
    }
}
