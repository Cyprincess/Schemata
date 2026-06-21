using Microsoft.Extensions.DependencyInjection;
using Schemata.Expressions.Skeleton;
using Xunit;

namespace Schemata.Expressions.Aip.Tests;

public class ExpressionLanguageBuilderExtensionsShould
{
    [Fact]
    public void Enables_Aip_And_Registers_Resolvable_Compiler() {
        var services = new ServiceCollection();
        var builder  = new FakeBuilder(services);

        builder.UseAip();

        var entry = Assert.Single(builder.Languages.Languages);
        Assert.Equal(AipLanguage.Name, entry.Language);

        var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetKeyedService<IExpressionCompiler>(AipLanguage.Name));
    }

    [Fact]
    public void Applies_Entry_Configuration() {
        var services = new ServiceCollection();
        var builder  = new FakeBuilder(services);

        builder.UseAip(e => e.Filtering = FilteringMode.Residual);

        Assert.Equal(FilteringMode.Residual, Assert.Single(builder.Languages.Languages).Filtering);
    }

    #region Nested type: FakeBuilder

    private sealed class FakeBuilder(IServiceCollection services) : IExpressionLanguageBuilder
    {
        public IServiceCollection Services { get; } = services;

        public ExpressionLanguageProfile Languages { get; } = new();
    }

    #endregion
}
