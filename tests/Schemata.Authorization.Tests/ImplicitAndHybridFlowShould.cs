using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Features;
using Schemata.Authorization.Skeleton.Advisors;
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
    public void Enable_Implicit_Response_Metadata_Without_A_Root_Registry() {
        var services = new ServiceCollection();

        new ImplicitFlowFeature().ConfigureServices(services, new SchemataOptions(), new Configurators());

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<SchemataAuthorizationOptions>>().Value;
        Assert.Contains(ResponseTypes.Token, options.AllowedResponseTypes);
        Assert.Contains(ResponseTypes.IdToken, options.AllowedResponseTypes);
        Assert.Contains("id_token token", options.AllowedResponseTypes);
        Assert.Contains(ResponseModes.Fragment, options.AllowedResponseModes);
        Assert.Contains(provider.GetServices<IDiscoveryAdvisor>(), advisor => advisor is AdviceDiscoveryImplicitGrant);
    }

    [Fact]
    public void Enable_Hybrid_Response_Metadata_Without_A_Root_Registry() {
        var services = new ServiceCollection();

        new HybridFlowFeature().ConfigureServices(services, new SchemataOptions(), new Configurators());

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<SchemataAuthorizationOptions>>().Value;
        Assert.Contains("code id_token", options.AllowedResponseTypes);
        Assert.Contains("code token", options.AllowedResponseTypes);
        Assert.Contains("code id_token token", options.AllowedResponseTypes);
        Assert.Contains(ResponseModes.Fragment, options.AllowedResponseModes);
        Assert.Contains(provider.GetServices<IDiscoveryAdvisor>(), advisor => advisor is AdviceDiscoveryImplicitGrant);
    }

    [Fact]
    public void Enable_None_Response_Metadata_Without_A_Root_Registry() {
        var services = new ServiceCollection();

        new NoneFlowFeature().ConfigureServices(services, new SchemataOptions(), new Configurators());

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<SchemataAuthorizationOptions>>().Value;
        Assert.Contains(ResponseTypes.None, options.AllowedResponseTypes);
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
