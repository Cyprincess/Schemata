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
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;

namespace Schemata.Authorization.Tests;

public class ClientSecretBasicHandlerShould
{
    private static readonly SchemataApplication TestApp = new() {
        Uid = Guid.NewGuid(), ClientId = "my-client", ClientType = "confidential",
    };

    private static ClientSecretBasicAuthentication<SchemataApplication> CreateHandler(
        Mock<IApplicationManager<SchemataApplication>>? managerMock = null,
        Mock<ISecurityStore<SchemataSecurity>>?         securities = null,
        Mock<ISecretVerifier>?                          verifier   = null
    ) {
        var mock     = managerMock ?? new Mock<IApplicationManager<SchemataApplication>>();
        securities ??= new();
        verifier   ??= new();
        var options = new SchemataAuthorizationOptions();
        return new(mock.Object, Options.Create(options), securities.Object, verifier.Object);
    }

    private static Mock<IApplicationManager<SchemataApplication>> MockManager(SchemataApplication? app = null) {
        var mock = new Mock<IApplicationManager<SchemataApplication>>();
        if (app is not null) {
            mock.Setup(m => m.FindByClientIdAsync(app.ClientId!, It.IsAny<CancellationToken>())).ReturnsAsync(app);
        }

        return mock;
    }

    private static (Mock<ISecurityStore<SchemataSecurity>> Securities, Mock<ISecretVerifier> Verifier) Credentials(
        string secret
    ) {
        var securities = new Mock<ISecurityStore<SchemataSecurity>>();
        securities
            .Setup(s => s.ListByParentAsync(
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Enumerate(new SchemataSecurity {
                Uid       = Guid.NewGuid(),
                Parent    = "applications/my-client",
                Kind      = SecurityConstants.Kinds.Password,
                Usage     = SecurityConstants.Usages.Authentication,
                Algorithm = SecurityConstants.Algorithms.Pbkdf2,
                Status    = SecurityConstants.Statuses.Valid,
                Value     = "stored-hash",
            }));

        var verifier = new Mock<ISecretVerifier>();
        verifier.Setup(v => v.VerifyAsync(It.IsAny<SchemataSecurity>(), secret, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        return (securities, verifier);
    }

    private static async IAsyncEnumerable<SchemataSecurity> Enumerate(params SchemataSecurity[] rows) {
        foreach (var row in rows) {
            yield return row;
        }
    }

    private static Dictionary<string, List<string?>> BasicHeader(string value) {
        return new() { ["Authorization"] = [value] };
    }

    private static string Encode(string raw) { return "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(raw)); }

    [Fact]
    public async Task Authenticates_FromValidBasicHeader() {
        var manager                = MockManager(TestApp);
        var (securities, verifier) = Credentials("my-secret");
        var handler                = CreateHandler(manager, securities, verifier);
        var headers                = BasicHeader(Encode("my-client:my-secret"));

        var result = await handler.AuthenticateAsync(null, null, headers, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("my-client", result.ClientId);
    }

    [Fact]
    public async Task Authenticates_WithUrlEncodedValues() {
        var app = new SchemataApplication {
            Uid = Guid.NewGuid(), ClientId = "my client", ClientType = "confidential",
        };
        var manager = new Mock<IApplicationManager<SchemataApplication>>();
        manager.Setup(m => m.FindByClientIdAsync("my client", It.IsAny<CancellationToken>())).ReturnsAsync(app);

        var (securities, verifier) = Credentials("my:secret");
        var handler                = CreateHandler(manager, securities, verifier);
        var headers                = BasicHeader(Encode("my%20client:my%3Asecret"));

        var result = await handler.AuthenticateAsync(null, null, headers, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("my client", result.ClientId);
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
                                                     null, null, headers, CancellationToken.None));
    }

    [Fact]
    public async Task Throws_WhenNoColonInDecoded() {
        var handler = CreateHandler();
        var headers = BasicHeader(Encode("client-without-secret"));

        await Assert.ThrowsAsync<OAuthException>(() => handler.AuthenticateAsync(
                                                     null, null, headers, CancellationToken.None));
    }
}
