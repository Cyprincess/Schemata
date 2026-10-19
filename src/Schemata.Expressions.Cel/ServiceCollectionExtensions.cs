using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Expressions.Skeleton;

namespace Schemata.Expressions.Cel;

/// <summary>
///     CEL expression language registration extensions.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the CEL filter compiler, the pushdown planner, and the language's global
    ///     defaults under <see cref="CelLanguage.Name" />. A call without
    ///     <paramref name="configure" /> keeps already-registered defaults, so module enablement
    ///     never resets host configuration; a call with <paramref name="configure" /> replaces them.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection" />.</param>
    /// <param name="configure">Configures the language's global defaults.</param>
    public static IServiceCollection AddCelExpressions(
        this IServiceCollection            services,
        Action<ExpressionLanguageOptions>? configure = null
    ) {
        var options = new ExpressionLanguageOptions();
        configure?.Invoke(options);

        services.TryAddKeyedSingleton<IExpressionCompiler, CelCompiler>(CelLanguage.Name);
        services.TryAddKeyedSingleton<IExpressionPushdownPlanner, CelPushdownPlanner>(CelLanguage.Name);
        services.TryAddKeyedSingleton<IExpressionReferenceProvider, CelReferenceProvider>(CelLanguage.Name);

        if (configure is not null || !services.Any(IsDescriptor)) {
            services.RemoveAllKeyed(typeof(ExpressionLanguageDescriptor), CelLanguage.Name);
            services.AddKeyedSingleton(CelLanguage.Name, new ExpressionLanguageDescriptor(CelLanguage.Name, options.Filtering, options.MaxResidualScanRows, true));
        }

        return services;
    }

    private static bool IsDescriptor(ServiceDescriptor descriptor) {
        return descriptor.ServiceType == typeof(ExpressionLanguageDescriptor) && Equals(descriptor.ServiceKey, CelLanguage.Name);
    }
}
