using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Expressions.Skeleton;
using Xunit;

namespace Schemata.Expressions.Aip.Tests;

public class ExpressionLanguageBuilderExtensionsShould
{
    [Fact]
    public void Enables_Aip_And_Registers_Resolvable_Compiler() {
        var services = new ServiceCollection();
        var languages = new ExpressionLanguageProfile();
        var builder = new Mock<IExpressionLanguageBuilder>(MockBehavior.Strict);
        builder.SetupGet(value => value.Services).Returns(services);
        builder.SetupGet(value => value.Languages).Returns(languages);

        builder.Object.UseAip();

        var entry = Assert.Single(languages.Languages);
        Assert.Equal(AipLanguage.Name, entry.Language);

        var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetKeyedService<IExpressionCompiler>(AipLanguage.Name));
    }

    [Fact]
    public void Applies_Entry_Configuration() {
        var services = new ServiceCollection();
        var languages = new ExpressionLanguageProfile();
        var builder = new Mock<IExpressionLanguageBuilder>(MockBehavior.Strict);
        builder.SetupGet(value => value.Services).Returns(services);
        builder.SetupGet(value => value.Languages).Returns(languages);

        builder.Object.UseAip(e => e.Filtering = FilteringMode.Residual);

        Assert.Equal(FilteringMode.Residual, Assert.Single(languages.Languages).Filtering);
    }
}
