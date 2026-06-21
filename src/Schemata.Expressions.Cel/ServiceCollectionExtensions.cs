using System;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Expressions.Skeleton;

namespace Schemata.Expressions.Cel;

/// <summary>
///     CEL expression language registration extensions.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the CEL filter compiler, the pushdown planner, and the language's global
    ///     defaults under <see cref="CelLanguage.Name" />.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection" />.</param>
    /// <param name="configure">Configures the language's global defaults.</param>
    public static IServiceCollection AddCelExpressions(
        this IServiceCollection            services,
        Action<ExpressionLanguageOptions>? configure = null
    ) {
        var options = new ExpressionLanguageOptions();
        configure?.Invoke(options);

        services.AddKeyedSingleton<IExpressionCompiler, CelCompiler>(CelLanguage.Name);
        services.AddKeyedSingleton<IExpressionPushdownPlanner, CelPushdownPlanner>(CelLanguage.Name);
        services.AddKeyedSingleton(CelLanguage.Name,
            new ExpressionLanguageDescriptor(CelLanguage.Name, options.Filtering, options.MaxResidualScanRows,
                                             SupportsValues: true));
        return services;
    }
}
