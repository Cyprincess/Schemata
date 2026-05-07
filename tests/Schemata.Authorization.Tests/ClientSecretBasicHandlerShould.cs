using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Xunit;

namespace Schemata.Authorization.Tests;

public class ClientSecretBasicHandlerShould
{
    private static readonly SchemataApplication TestApp = new() {
        Uid = Guid.NewGuid(), ClientId = "my-client", ClientType = "confidential",
    };

    private static ClientSecretBasicAuthentication<SchemataApplication> CreateHandler(
        Mock<IApplicationManager<SchemataApplication>>? managerMock = null
    ) {
        var mock    = managerMock ?? new Mock<IApplicationManager<SchemataApplication>>();
        var options = new SchemataAuthorizationOptions();
        return new(mock.Object, Options.Create(options));
    }

    private static Mock<IApplicationManager<SchemataApplication>> MockManager(SchemataApplication? app = null) {
        var mock = new Mock<IApplicationManager<SchemataApplication>>();
        if (app is not null) {
            mock.Setup(m => m.FindByClientIdAsync(app.ClientId!, It.IsAny<CancellationToken>())).ReturnsAsync(app);
            mock.Setup(m => m.ValidateClientSecretAsync(app, "my-secret", It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
        }

        return mock;
    }

    private static Dictionary<string, List<string?>> BasicHeader(string value) {
        return new() { ["Authorization"] = [value] };
    }

    private static string Encode(string raw) { return "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(raw)); }

    [Fact]
    public async Task Authenticates_FromValidBasicHeader() {
        var manager = MockManager(TestApp);
        var handler = CreateHandler(manager);
        var headers = BasicHeader(Encode("my-client:my-secret"));

        var result = await handler.AuthenticateAsync(null, null, headers, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("my-client", result!.ClientId);
    }

    [Fact]
    public async Task Authenticates_WithUrlEncodedValues() {
        var app = new SchemataApplication { Uid = Guid.NewGuid(), ClientId = "my client", ClientType = "confidential" };
        var manager = new Mock<IApplicationManager<SchemataApplication>>();
        manager.Setup(m => m.FindByClientIdAsync("my client", It.IsAny<CancellationToken>())).ReturnsAsync(app);
        manager.Setup(m => m.ValidateClientSecretAsync(app, "my:secret", It.IsAny<CancellationToken>()))
               .ReturnsAsync(true);

        var handler = CreateHandler(manager);
        var headers = BasicHeader(Encode("my%20client:my%3Asecret"));

        var result = await handler.AuthenticateAsync(null, null, headers, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("my client", result!.ClientId);
    }

    [Fact]
    public async Task ReturnsNull_WhenNoAuthorizationHeader() {
        var handler = CreateHandler();

        var result = await handler.AuthenticateAsync(null, null, new(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ReturnsNull_WhenNotBasicScheme() {
        var handler = CreateHandler();
        var headers = BasicHeader("Bearer some-token");

        var result = await handler.AuthenticateAsync(null, null, headers, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Throws_WhenInvalidBase64() {
        var handler = CreateHandler();
        var headers = BasicHeader("Basic !!!not-base64!!!");

        await Assert.ThrowsAsync<OAuthException>(() => handler.AuthenticateAsync(
                                                     null,
                                                     null,
                                                     headers,
                                                     CancellationToken.None
                                                 )
        );
    }

    [Fact]
    public async Task Throws_WhenNoColonInDecoded() {
        var handler = CreateHandler();
        var headers = BasicHeader(Encode("client-without-secret"));

        await Assert.ThrowsAsync<OAuthException>(() => handler.AuthenticateAsync(
                                                     null,
                                                     null,
                                                     headers,
                                                     CancellationToken.None
                                                 )
        );
    }
}
