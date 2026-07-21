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
}
