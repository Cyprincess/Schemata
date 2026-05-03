using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Tests;

/// <summary>
///     Tests for the client_credentials grant handler including client authentication
///     (via IClientAuthenticationService) and grant permission checks (via the advisor pipeline).
/// </summary>
public class ClientCredentialsHandlerShould
{
    private static SchemataApplication CreateApplication(
        string type     = "confidential",
        bool   hasGrant = true,
        string clientId = "test-client",
        long   id       = 42
    ) {
        var app = new SchemataApplication {
            Id          = id,
            ClientId    = clientId,
            ClientType  = type,
            Permissions = new List<string>(),
        };

        if (hasGrant) {
            app.Permissions.Add("g:client_credentials");
        }

        return app;
    }

    private static (ClientCredentialsHandler<SchemataApplication> handler,
        Mock<IClientAuthenticationService<SchemataApplication>> clientAuth,
        Mock<IApplicationManager<SchemataApplication>> manager) CreateHandler(
            SchemataApplication? application = null,
            bool                 authFails   = false,
            string               errorCode   = OAuthErrors.InvalidClient
        ) {
        var clientAuth = new Mock<IClientAuthenticationService<SchemataApplication>>(MockBehavior.Strict);
        var manager    = new Mock<IApplicationManager<SchemataApplication>>(MockBehavior.Strict);

        if (authFails) {
            clientAuth.Setup(c => c.AuthenticateAsync(
                                 It.IsAny<Dictionary<string, List<string?>>?>(),
                                 It.IsAny<Dictionary<string, List<string?>>?>(),
                                 It.IsAny<Dictionary<string, List<string?>>?>(),
                                 It.IsAny<CancellationToken>()
                             )
                       )
                      .ThrowsAsync(new OAuthException(errorCode, "auth failed"));
        } else if (application is not null) {
            clientAuth.Setup(c => c.AuthenticateAsync(
                                 It.IsAny<Dictionary<string, List<string?>>?>(),
                                 It.IsAny<Dictionary<string, List<string?>>?>(),
                                 It.IsAny<Dictionary<string, List<string?>>?>(),
                                 It.IsAny<CancellationToken>()
                             )
                       )
                      .ReturnsAsync(application);

            foreach (var perm in application.Permissions!) {
                manager.Setup(m => m.HasPermissionAsync(application, perm, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(true);
            }
        }

        var services = new ServiceCollection();
        services.AddSingleton(manager.Object);
        services.TryAddEnumerable(
            ServiceDescriptor
               .Scoped<ITokenRequestAdvisor<SchemataApplication>, AdviceTokenGrantPermission<SchemataApplication>>()
        );
        var sp = services.BuildServiceProvider();

        var handler = new ClientCredentialsHandler<SchemataApplication>(clientAuth.Object, sp);
        return (handler, clientAuth, manager);
    }

    private static TokenRequest CreateRequest(string clientId = "test-client", string secret = "correct-secret") {
        return new() {
            ClientId = clientId, ClientSecret = secret, GrantType = GrantTypes.ClientCredentials,
        };
    }

    // -- Handler tests -------------------------------------------

    [Fact]
    public async Task AcceptValidConfidentialClient() {
        var application = CreateApplication();
        var (handler, _, _) = CreateHandler(application);
        var request = CreateRequest();

        var result = await handler.HandleAsync(request, null, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result.Principal);
    }

    [Fact]
    public async Task RejectUnknownClient() {
        var (handler, _, _) = CreateHandler(authFails: true);
        var request = CreateRequest("unknown");

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.HandleAsync(
                                                              request,
                                                              null,
                                                              CancellationToken.None
                                                          )
        );
        Assert.Equal(OAuthErrors.InvalidClient, ex.Code);
    }

    [Fact]
    public async Task RejectConfidentialClientWithoutSecret() {
        var (handler, _, _) = CreateHandler(authFails: true);
        var request = CreateRequest(secret: string.Empty);

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.HandleAsync(
                                                              request,
                                                              null,
                                                              CancellationToken.None
                                                          )
        );
        Assert.Equal(OAuthErrors.InvalidClient, ex.Code);
    }

    [Fact]
    public async Task AcceptPublicClientWithoutSecret() {
        var application = CreateApplication(ClientTypes.Public);
        var (handler, _, _) = CreateHandler(application);
        var request = CreateRequest(secret: string.Empty);

        var result = await handler.HandleAsync(request, null, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result.Principal);
    }

    [Fact]
    public async Task RejectInvalidSecret() {
        var (handler, _, _) = CreateHandler(authFails: true);
        var request = CreateRequest(secret: "wrong-secret");

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.HandleAsync(
                                                              request,
                                                              null,
                                                              CancellationToken.None
                                                          )
        );
        Assert.Equal(OAuthErrors.InvalidClient, ex.Code);
    }

    [Fact]
    public async Task RejectClientWithoutGrantPermission() {
        var application = CreateApplication(hasGrant: false);
        var (handler, _, manager) = CreateHandler(application);

        manager.Setup(m => m.HasPermissionAsync(application, "g:client_credentials", It.IsAny<CancellationToken>()))
               .ReturnsAsync(false);

        var request = CreateRequest();

        var ex = await Assert.ThrowsAsync<OAuthException>(() => handler.HandleAsync(
                                                              request,
                                                              null,
                                                              CancellationToken.None
                                                          )
        );
        Assert.Equal(OAuthErrors.UnauthorizedClient, ex.Code);
    }
}
