using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Features;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class PairwiseFeatureShould
{
    [Fact]
    public void Register_The_Pairwise_Machinery() {
        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(new SchemataAuthorizationOptions()));
        new PairwiseFeature<SchemataApplication>().ConfigureServices(services, new(), new());

        Assert.Contains(services, d => d.ServiceType == typeof(IClaimsAdvisor)
                                   && d.ImplementationType == typeof(AdviceClaimsPairwise<SchemataApplication>));
        Assert.Contains(services, d => d.ServiceType == typeof(PairwiseSubjectTranslator<SchemataApplication>));
        Assert.Contains(services, d => d.ServiceType == typeof(IPairwiseSubjectTranslator));

        using var provider = services.BuildServiceProvider();
        Assert.Contains(
            provider.GetRequiredService<IEnumerable<IDiscoveryAdvisor>>(),
            advisor => advisor is AdviceDiscoveryPairwise);
    }

    [Fact]
    public async Task Advertise_Pairwise_Discovery_When_A_Salt_Is_Configured() {
        var discovery = await Advise_Discovery(new() { PairwiseSalt = "salt" });

        Assert.Equal([SubjectTypes.Public, SubjectTypes.Pairwise], discovery.Document!.SubjectTypesSupported);
    }

    [Fact]
    public async Task Keep_The_Discovery_Document_Public_Only_Without_A_Salt() {
        var discovery = await Advise_Discovery(new());

        Assert.Equal([SubjectTypes.Public], discovery.Document!.SubjectTypesSupported);
    }

    private static async Task<DiscoveryContext> Advise_Discovery(SchemataAuthorizationOptions options) {
        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(options));
        new PairwiseFeature<SchemataApplication>().ConfigureServices(services, new(), new());
        using var provider = services.BuildServiceProvider();

        // Mirrors the DiscoveryHandler baseline: public subjects before the feature advisors run.
        var discovery = new DiscoveryContext {
            Document = new() { SubjectTypesSupported = [SubjectTypes.Public] },
        };

        foreach (var advisor in provider.GetRequiredService<IEnumerable<IDiscoveryAdvisor>>()) {
            await advisor.AdviseAsync(new(provider), discovery, CancellationToken.None);
        }

        return discovery;
    }
}
