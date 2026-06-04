using Microsoft.Extensions.DependencyInjection;
using Schemata.Expressions.Skeleton;

namespace Schemata.Expressions.Aip;

/// <summary>
///     AIP expression language registration extensions.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the AIP-160 filter and AIP-132 order-by compilers under
    ///     <see cref="AipLanguage.Name" />.
    /// </summary>
    public static IServiceCollection AddAipExpressions(this IServiceCollection services) {
        services.AddKeyedSingleton<IExpressionCompiler, AipCompiler>(AipLanguage.Name);
        services.AddKeyedSingleton<IOrderCompiler, AipOrderCompiler>(AipLanguage.Name);
        return services;
    }
}
