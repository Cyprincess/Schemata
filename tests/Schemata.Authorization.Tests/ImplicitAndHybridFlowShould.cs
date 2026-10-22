using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Features;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Abstractions.Advisors;
using Schemata.Core;
using System.Threading.Tasks;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class ImplicitAndHybridFlowShould
{
    [Fact]
    public void Reject_An_Implicit_Flow_Without_The_Authorization_Code_Endpoint() {
        var options = new SchemataOptions();
        var services = new ServiceCollection();

        var error = Assert.Throws<InvalidOperationException>(() =>
            new ImplicitFlowFeature().ConfigureServices(services, options, new Configurators()));

        Assert.Contains("UseAuthorizationCodeFlow", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Reject_A_Hybrid_Flow_Without_The_Authorization_Code_Endpoint() {
        var options = new SchemataOptions();
        var services = new ServiceCollection();

        var error = Assert.Throws<InvalidOperationException>(() =>
            new HybridFlowFeature().ConfigureServices(services, options, new Configurators()));

        Assert.Contains("UseAuthorizationCodeFlow", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Reject_A_None_Flow_Without_The_Authorization_Code_Endpoint() {
        var options = new SchemataOptions();
        var services = new ServiceCollection();

        var error = Assert.Throws<InvalidOperationException>(() =>
            new NoneFlowFeature().ConfigureServices(services, options, new Configurators()));

        Assert.Contains("UseAuthorizationCodeFlow", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Advertise_The_Implicit_Grant_Only_When_A_Degradation_Flow_Installs_The_Advisor() {
        var ctx = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var document = new DiscoveryDocument {
            GrantTypesSupported = [],
        };
        var discovery = new DiscoveryContext { Document = document };

        await new AdviceDiscoveryImplicitGrant().AdviseAsync(ctx, discovery);

        Assert.Contains(GrantTypes.Implicit, document.GrantTypesSupported!);
    }

    [Fact]
    public async Task Not_Advertise_The_Implicit_Grant_For_The_Default_Code_Flow() {
        var ctx = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var document = new DiscoveryDocument();
        var discovery = new DiscoveryContext { Document = document, Issuer = "https://issuer.example" };
        var advisor = new AdviceDiscoveryCodeFlow(Options.Create(new CodeFlowOptions()));

        await advisor.AdviseAsync(ctx, discovery);

        Assert.NotNull(document.GrantTypesSupported);
        Assert.Contains(GrantTypes.AuthorizationCode, document.GrantTypesSupported!);
        Assert.DoesNotContain(GrantTypes.Implicit, document.GrantTypesSupported!);
    }
}
