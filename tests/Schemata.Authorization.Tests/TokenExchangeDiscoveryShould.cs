using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Authorization.Foundation.Features;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class TokenExchangeDiscoveryShould
{
    [Fact]
    public async Task Advertise_The_Grant_When_No_Exchange_Handler_Is_Registered() {
        var discovery = await AdviseDiscovery(new());

        Assert.Contains(GrantTypes.TokenExchange, discovery.Document!.GrantTypesSupported!);
    }

    [Fact]
    public async Task Advertise_The_Grant_When_Only_Keyed_Handlers_Are_Registered() {
        var services = new ServiceCollection();
        services.AddKeyedScoped<ITokenExchangeHandler<SchemataApplication>>(
            $"{TokenTypeUris.IdToken}|{TokenTypeUris.AccessToken}",
            (_, _) => Mock.Of<ITokenExchangeHandler<SchemataApplication>>());

        var discovery = await AdviseDiscovery(services);

        Assert.Contains(GrantTypes.TokenExchange, discovery.Document!.GrantTypesSupported!);
    }

    private static async Task<DiscoveryContext> AdviseDiscovery(ServiceCollection services) {
        new TokenExchangeFeature<SchemataApplication>().ConfigureServices(services, new(), new());
        await using var provider = services.BuildServiceProvider();

        var discovery = new DiscoveryContext();
        foreach (var advisor in provider.GetRequiredService<IEnumerable<IDiscoveryAdvisor>>()) {
            await advisor.AdviseAsync(new(provider), discovery, CancellationToken.None);
        }

        return discovery;
    }
}
