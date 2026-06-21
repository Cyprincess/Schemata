using System;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Expressions.Skeleton;

namespace Schemata.Expressions.Aip;

/// <summary>
///     AIP expression language registration extensions.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the AIP-160 filter compiler, the filter pushdown planner, and the language's
    ///     global defaults under <see cref="AipLanguage.Name" />.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection" />.</param>
    /// <param name="configure">Configures the language's global defaults.</param>
    public static IServiceCollection AddAipExpressions(
        this IServiceCollection            services,
        Action<ExpressionLanguageOptions>? configure = null
    ) {
        var options = new ExpressionLanguageOptions();
        configure?.Invoke(options);

        services.AddKeyedSingleton<IExpressionCompiler, AipCompiler>(AipLanguage.Name);
        services.AddKeyedSingleton<IExpressionPushdownPlanner, AipPushdownPlanner>(AipLanguage.Name);
        services.AddKeyedSingleton(AipLanguage.Name,
            new ExpressionLanguageDescriptor(AipLanguage.Name, options.Filtering, options.MaxResidualScanRows));
        return services;
    }
}
