using Microsoft.Extensions.DependencyInjection;
using Schemata.Expressions.Skeleton;
using Xunit;

namespace Schemata.Expressions.Cel.Tests;

public class ServiceCollectionExtensionsShould
{
    [Fact]
    public void Registers_Configured_Descriptor_Under_Language_Key() {
        var services = new ServiceCollection();
        services.AddCelExpressions(o => o.Filtering = FilteringMode.Residual);
        var provider = services.BuildServiceProvider();

        var descriptor = provider.GetKeyedService<ExpressionLanguageDescriptor>(CelLanguage.Name);

        Assert.NotNull(descriptor);
        Assert.Equal(CelLanguage.Name, descriptor.Language);
        Assert.Equal(FilteringMode.Residual, descriptor.Filtering);
        Assert.True(descriptor.SupportsValues);
    }

    [Fact]
    public void Registers_Default_Descriptor_When_Not_Configured() {
        var services = new ServiceCollection();
        services.AddCelExpressions();
        var provider = services.BuildServiceProvider();

        var descriptor = provider.GetKeyedService<ExpressionLanguageDescriptor>(CelLanguage.Name);

        Assert.NotNull(descriptor);
        Assert.Equal(FilteringMode.Default, descriptor.Filtering);
    }
}
