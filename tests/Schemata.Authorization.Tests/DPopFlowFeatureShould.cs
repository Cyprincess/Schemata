using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Features;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Core;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class DPopFlowFeatureShould
{
    [Fact]
    public async Task Register_The_Dpop_Machinery_And_Apply_The_Configured_Options() {
        var services      = new ServiceCollection();
        var configurators = new Configurators();
        configurators.Set<DPopOptions>(options => {
            options.ProofTimeWindow      = TimeSpan.FromSeconds(77);
            options.NonceLifetime       = TimeSpan.FromMinutes(9);
            options.RequireForAllClients();
        });

        new DPopFlowFeature<SchemataApplication>().ConfigureServices(services, new(), configurators);

        // Mirrors SchemataBuilder.Invoke: the features configure first, then each outstanding
        // configurator is bound as IConfigureOptions<TOptions>. Configurators.Invoke itself is
        // internal to Schemata.Core.
        services.Configure(configurators.PopOrDefault<DPopOptions>());

        // Registration presence is the feature's contract; the advisor graph itself needs host
        // services (ICacheProvider) the bare collection does not carry.
        Assert.Contains(services, d => d.ServiceType == typeof(DPopProofValidator));
        Assert.Contains(services, d => d.ServiceType == typeof(ITokenRequestAdvisor<SchemataApplication>)
                                   && d.ImplementationType == typeof(AdviceRequestDpop<SchemataApplication>));
        Assert.Contains(services, d => d.ServiceType == typeof(IAuthorizeAdvisor<SchemataApplication>)
                                   && d.ImplementationType == typeof(AdviceAuthorizeDpopJkt<SchemataApplication>));

        using var provider = services.BuildServiceProvider();

        Assert.Contains(
            provider.GetRequiredService<IEnumerable<IDiscoveryAdvisor>>(),
            advisor => advisor is AdviceDiscoveryDpop);

        var schemes = await provider.GetRequiredService<IAuthenticationSchemeProvider>().GetAllSchemesAsync();
        Assert.Contains(schemes, scheme => scheme.Name == Schemes.Dpop);

        var dpop = provider.GetRequiredService<IOptions<DPopOptions>>().Value;
        Assert.True(dpop.RequireAllClients);
        Assert.Equal(TimeSpan.FromSeconds(77), dpop.ProofTimeWindow);
        Assert.Equal(TimeSpan.FromMinutes(9), dpop.NonceLifetime);
    }
    [Fact]
    public void Keep_The_Discovery_Document_And_Options_Defaults_Without_The_Feature() {
        var services = new ServiceCollection();
        services.AddOptions();

        using var provider = services.BuildServiceProvider();

        Assert.DoesNotContain(
            provider.GetRequiredService<IEnumerable<IDiscoveryAdvisor>>(),
            advisor => advisor is AdviceDiscoveryDpop);

        var dpop = provider.GetRequiredService<IOptions<DPopOptions>>().Value;
        Assert.Equal(TimeSpan.FromSeconds(30), dpop.ProofTimeWindow);
        Assert.Equal(TimeSpan.FromMinutes(5), dpop.NonceLifetime);
        Assert.False(dpop.RequireAllClients);
    }
    [Fact]
    public async Task Source_The_Discovery_Field_From_The_Configured_Algorithms() {
        var options = new DPopOptions();
        options.SigningAlgorithms.Clear();
        options.SigningAlgorithms.Add("RS256");

        var discovery = new DiscoveryContext();
        await new AdviceDiscoveryDpop(Options.Create(options))
            .AdviseAsync(new(new ServiceCollection().BuildServiceProvider()), discovery);

        Assert.Equal(["RS256"], discovery.Document!.DpopSigningAlgValuesSupported);
    }
    [Fact]
    public async Task Skip_The_Discovery_Field_When_No_Algorithms_Are_Configured() {
        var options = new DPopOptions();
        options.SigningAlgorithms.Clear();

        var discovery = new DiscoveryContext();
        await new AdviceDiscoveryDpop(Options.Create(options))
            .AdviseAsync(new(new ServiceCollection().BuildServiceProvider()), discovery);

        Assert.Null(discovery.Document);
    }
}
