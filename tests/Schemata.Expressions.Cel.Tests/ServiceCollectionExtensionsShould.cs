using System.Linq;
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

    [Fact]
    public void Keeps_Configured_Defaults_When_Called_Again_Without_Configure() {
        var services = new ServiceCollection();
        services.AddCelExpressions(o => {
            o.Filtering           = FilteringMode.Residual;
            o.MaxResidualScanRows = 5_000;
        });
        services.AddCelExpressions();

        var descriptor = services.BuildServiceProvider().GetKeyedService<ExpressionLanguageDescriptor>(CelLanguage.Name);

        Assert.NotNull(descriptor);
        Assert.Equal(FilteringMode.Residual, descriptor.Filtering);
        Assert.Equal(5_000, descriptor.MaxResidualScanRows);
    }

    [Fact]
    public void Replaces_Defaults_When_Reconfigured() {
        var services = new ServiceCollection();
        services.AddCelExpressions(o => o.Filtering = FilteringMode.Residual);
        services.AddCelExpressions(o => o.Filtering = FilteringMode.Strict);

        var descriptor = services.BuildServiceProvider().GetKeyedService<ExpressionLanguageDescriptor>(CelLanguage.Name);

        Assert.NotNull(descriptor);
        Assert.Equal(FilteringMode.Strict, descriptor.Filtering);
    }

    [Fact]
    public void Registers_Services_Once_Across_Repeated_Calls() {
        var services = new ServiceCollection();

        services.AddCelExpressions();
        services.AddCelExpressions();

        Assert.Single(services, d => d.ServiceType == typeof(IExpressionCompiler) && Equals(d.ServiceKey, CelLanguage.Name));
        Assert.Single(services, d => d.ServiceType == typeof(IExpressionPushdownPlanner) && Equals(d.ServiceKey, CelLanguage.Name));
        Assert.Single(services, d => d.ServiceType == typeof(IExpressionReferenceProvider) && Equals(d.ServiceKey, CelLanguage.Name));
        Assert.Single(services, d => d.ServiceType == typeof(ExpressionLanguageDescriptor) && Equals(d.ServiceKey, CelLanguage.Name));
    }
}
