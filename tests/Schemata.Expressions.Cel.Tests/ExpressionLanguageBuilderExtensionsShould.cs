using Microsoft.Extensions.DependencyInjection;
using Schemata.Expressions.Skeleton;
using Xunit;

namespace Schemata.Expressions.Cel.Tests;

public class ExpressionLanguageBuilderExtensionsShould
{
    [Fact]
    public void Enables_Cel_And_Registers_Resolvable_Compiler() {
        var services = new ServiceCollection();
        var builder  = new FakeBuilder(services);

        builder.UseCel(e => e.Filtering = FilteringMode.Residual);

        var entry = Assert.Single(builder.Languages.Languages);
        Assert.Equal(CelLanguage.Name, entry.Language);
        Assert.Equal(FilteringMode.Residual, entry.Filtering);

        var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetKeyedService<IExpressionCompiler>(CelLanguage.Name));
    }

    #region Nested type: FakeBuilder

    private sealed class FakeBuilder(IServiceCollection services) : IExpressionLanguageBuilder
    {
        public IServiceCollection Services { get; } = services;

        public ExpressionLanguageProfile Languages { get; } = new();
    }

    #endregion
}
