using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Expressions.Skeleton;
using Xunit;

namespace Schemata.Expressions.Cel.Tests;

public class ExpressionLanguageBuilderExtensionsShould
{
    [Fact]
    public void Enables_Cel_And_Registers_Resolvable_Compiler() {
        var services = new ServiceCollection();
        var languages = new ExpressionLanguageProfile();
        var builder = new Mock<IExpressionLanguageBuilder>(MockBehavior.Strict);
        builder.SetupGet(value => value.Services).Returns(services);
        builder.SetupGet(value => value.Languages).Returns(languages);

        builder.Object.UseCel(e => e.Filtering = FilteringMode.Residual);

        var entry = Assert.Single(languages.Languages);
        Assert.Equal(CelLanguage.Name, entry.Language);
        Assert.Equal(FilteringMode.Residual, entry.Filtering);

        var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetKeyedService<IExpressionCompiler>(CelLanguage.Name));
    }

    [Fact]
    public void Keeps_Global_Defaults_When_Enabled_Without_Configure() {
        var services = new ServiceCollection();
        services.AddCelExpressions(o => {
            o.Filtering           = FilteringMode.Residual;
            o.MaxResidualScanRows = 5_000;
        });
        var languages = new ExpressionLanguageProfile();
        var builder = new Mock<IExpressionLanguageBuilder>(MockBehavior.Strict);
        builder.SetupGet(value => value.Services).Returns(services);
        builder.SetupGet(value => value.Languages).Returns(languages);

        builder.Object.UseCel();

        var descriptor = services.BuildServiceProvider().GetKeyedService<ExpressionLanguageDescriptor>(CelLanguage.Name);
        Assert.NotNull(descriptor);
        Assert.Equal(FilteringMode.Residual, descriptor.Filtering);
        Assert.Equal(5_000, descriptor.MaxResidualScanRows);
    }
}
