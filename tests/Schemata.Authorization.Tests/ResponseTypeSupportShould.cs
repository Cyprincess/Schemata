using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Security.Skeleton.Entities;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class ResponseTypeSupportShould
{
    // Configured with reversed component order to prove discovery and the authorization
    // endpoint compare the same canonical combinations.
    private static readonly string[] Configured = [
        ResponseTypes.Code,
        ResponseTypes.Token,
        ResponseTypes.IdToken,
        ResponseTypes.Token + " " + ResponseTypes.IdToken,
        ResponseTypes.IdToken + " " + ResponseTypes.Code,
        ResponseTypes.Token + " " + ResponseTypes.Code,
        ResponseTypes.Token + " " + ResponseTypes.IdToken + " " + ResponseTypes.Code,
    ];

    [Fact]
    public async Task Advertise_Exactly_The_Response_Types_The_Authorization_Endpoint_Accepts() {
        var options = new SchemataAuthorizationOptions();
        foreach (var responseType in Configured) {
            options.AllowedResponseTypes.Add(responseType);
        }

        var document = await DiscoverAsync(options);
        var advertised = document.ResponseTypesSupported;

        Assert.NotNull(advertised);
        Assert.Equal(
            ResponseTypeGrantTypes.Requirements.Keys.Order(StringComparer.Ordinal),
            advertised.Order(StringComparer.Ordinal));

        foreach (var responseType in advertised) {
            Assert.True(await AcceptsAsync(options, responseType), $"the authorization endpoint rejected {responseType}");
        }
    }

    [Fact]
    public async Task Keep_An_Enabled_Extension_Response_Type_Across_Discovery_And_Authorization() {
        var options = new SchemataAuthorizationOptions();
        options.AllowedResponseTypes.Add("none");

        var document = await DiscoverAsync(options);

        Assert.Equal(["none"], document.ResponseTypesSupported);
        Assert.True(await AcceptsAsync(options, "none"));
    }

    private static async Task<DiscoveryDocument> DiscoverAsync(SchemataAuthorizationOptions options) {
        var services = new ServiceCollection();
        services.AddSingleton<IOptions<SchemataAuthorizationOptions>>(Options.Create(options));
        await using var provider = services.BuildServiceProvider();

        var scopes = new Mock<IScopeManager<SchemataScope>>();
        scopes.Setup(s => s.ListAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
              .Returns(EmptyScopesAsync());

        var handler = new DiscoveryHandler<SchemataScope, SchemataSecurity>(
            Options.Create(options),
            TestSecurityKeys.CreateTokenService(options, seed: false),
            scopes.Object);

        var advice = new AdviceContext(provider);
        var       result  = await handler.GetDiscoveryDocumentAsync("https://op.example/connect", false, advice, CancellationToken.None);

        return Assert.IsType<DiscoveryDocument>(result.Data);
    }

    private static async Task<bool> AcceptsAsync(SchemataAuthorizationOptions options, string responseType) {
        var application = new SchemataApplication {
            ClientId      = "test",
            ResponseTypes = [responseType],
        };

        var manager = new Mock<IApplicationManager<SchemataApplication>>();
        manager.SetupTypedMetadata();
        manager.Setup(m => m.FindByClientIdAsync("test", It.IsAny<CancellationToken>())).ReturnsAsync(application);
        manager.Setup(m => m.ValidateRedirectUriAsync(application, "https://example.com/cb", It.IsAny<CancellationToken>()))
               .ReturnsAsync(true);

        await using var provider = new ServiceCollection().BuildServiceProvider();
        var advisor = new AdviceAuthorizeClientAndRedirect<SchemataApplication>(manager.Object, Options.Create(options));
        var request = new AuthorizeRequest {
            ClientId     = "test",
            RedirectUri  = "https://example.com/cb",
            ResponseType = responseType,
            Nonce        = "n",
        };

        var result = await advisor.AdviseAsync(new(provider), new() { Request = request });

        return result == AdviseResult.Continue;
    }

    private static async IAsyncEnumerable<SchemataScope> EmptyScopesAsync() {
        await Task.CompletedTask;
        yield break;
    }
}
