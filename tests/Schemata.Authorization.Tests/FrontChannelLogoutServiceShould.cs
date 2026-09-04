using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Security.Skeleton.Services;
using Xunit;

namespace Schemata.Authorization.Tests;

public class FrontChannelLogoutServiceShould
{
    private static IAsyncEnumerable<SchemataToken> NoTokens() {
        return EmptyTokens();
    }

    private static async IAsyncEnumerable<SchemataToken> EmptyTokens() {
        await Task.CompletedTask;
        yield break;
    }

    private static async IAsyncEnumerable<SchemataApplication> OneApp() {
        await Task.CompletedTask;
        yield return new() { ClientId = "client-1", FrontChannelLogoutUri = "https://rp.example/logout" };
    }

    private static FrontChannelLogoutService<SchemataApplication> Create(
        Mock<ITokenStore<SchemataToken>> tokens,
        Mock<IApplicationManager<SchemataApplication>> apps) {
        return new(apps.Object, tokens.Object,
            Options.Create(new SchemataAuthorizationOptions { Issuer = "https://as.example" }));
    }

    private static (Mock<ITokenStore<SchemataToken>> Tokens, Mock<IApplicationManager<SchemataApplication>> Apps)
        Mocks() {
        var tokens = new Mock<ITokenStore<SchemataToken>>();
        tokens.Setup(m => m.ListBySessionAsync("sid-1", It.IsAny<CancellationToken>())).Returns(NoTokens);
        tokens.Setup(m => m.ListByParentAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).Returns(NoTokens);
        var apps = new Mock<IApplicationManager<SchemataApplication>>();
        apps.Setup(m => m.ListAsync(It.IsAny<Func<IQueryable<SchemataApplication>, IQueryable<SchemataApplication>>>(),
                                    It.IsAny<CancellationToken>())).Returns(OneApp());
        return (tokens, apps);
    }

    [Fact]
    public async Task Append_Iss_And_Sid_Together_When_A_Session_Is_Present() {
        var (tokens, apps) = Mocks();
        var service = Create(tokens, apps);

        var uris = await service.GetFrontChannelUrisAsync("user-1", "sid-1");

        var uri = Assert.Single(uris);
        Assert.Contains("iss=https%3A%2F%2Fas.example", uri);
        Assert.Contains("sid=sid-1", uri);
    }

    [Fact]
    public async Task Append_Neither_Iss_Nor_Sid_Without_A_Session() {
        var (tokens, apps) = Mocks();
        var service = Create(tokens, apps);

        var uris = await service.GetFrontChannelUrisAsync("user-1", null);

        Assert.Equal("https://rp.example/logout", Assert.Single(uris));
    }
}