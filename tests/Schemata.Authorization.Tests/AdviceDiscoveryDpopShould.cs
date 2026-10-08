using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton.Contexts;
using Xunit;

namespace Schemata.Authorization.Tests;

public class AdviceDiscoveryDpopShould
{
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
