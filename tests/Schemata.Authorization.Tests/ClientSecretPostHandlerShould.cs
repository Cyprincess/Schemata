using System.Collections.Generic;
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
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Tests;

public class ClientSecretPostHandlerShould
{
    private static readonly SchemataApplication TestApp = new() {
        Id = 1, ClientId = "my-client", ClientType = "confidential",
    };

    private static ClientSecretPostAuthentication<SchemataApplication> CreateHandler(
        Mock<IApplicationManager<SchemataApplication>>? managerMock = null
    ) {
        var mock    = managerMock ?? new Mock<IApplicationManager<SchemataApplication>>();
        var options = new SchemataAuthorizationOptions();
        options.AllowedClientAuthMethods.Add(ClientAuthMethods.ClientSecretPost);
        return new(mock.Object, Options.Create(options));
    }

    private static Mock<IApplicationManager<SchemataApplication>> MockManager() {
        var mock = new Mock<IApplicationManager<SchemataApplication>>();
        mock.Setup(m => m.FindByCanonicalNameAsync("my-client", It.IsAny<CancellationToken>())).ReturnsAsync(TestApp);
        mock.Setup(m => m.ValidateClientSecretAsync(TestApp, "my-secret", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return mock;
    }

    private static Dictionary<string, List<string?>> Form(string clientId, string? secret) {
        var form = new Dictionary<string, List<string?>> { [Parameters.ClientId] = [clientId] };
        if (secret is not null) {
            form[Parameters.ClientSecret] = [secret];
        }

        return form;
    }

    [Fact]
    public async Task Authenticates_FromPostBody() {
        var manager = MockManager();
        var handler = CreateHandler(manager);

        var result = await handler.AuthenticateAsync(
            null,
            Form("my-client", "my-secret"),
            null,
            CancellationToken.None
        );

        Assert.NotNull(result);
        Assert.Equal("my-client", result!.ClientId);
    }

    [Fact]
    public async Task Throws_WhenClientSecretEmpty() {
        var manager = MockManager();
        var handler = CreateHandler(manager);

        await Assert.ThrowsAsync<OAuthException>(() => handler.AuthenticateAsync(
                                                     null,
                                                     Form("my-client", ""),
                                                     null,
                                                     CancellationToken.None
                                                 )
        );
    }

    [Fact]
    public async Task Throws_WhenClientSecretNull() {
        var manager = MockManager();
        var handler = CreateHandler(manager);

        await Assert.ThrowsAsync<OAuthException>(() => handler.AuthenticateAsync(
                                                     null,
                                                     Form("my-client", null),
                                                     null,
                                                     CancellationToken.None
                                                 )
        );
    }

    [Fact]
    public async Task Throws_WhenBothEmpty() {
        var handler = CreateHandler();

        await Assert.ThrowsAsync<OAuthException>(() => handler.AuthenticateAsync(
                                                     null,
                                                     Form("", ""),
                                                     null,
                                                     CancellationToken.None
                                                 )
        );
    }
}
