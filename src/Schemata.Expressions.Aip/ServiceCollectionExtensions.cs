using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Expressions.Skeleton;

namespace Schemata.Expressions.Aip;

/// <summary>
///     AIP expression language registration extensions.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the AIP-160 filter compiler, the filter pushdown planner, and the language's
    ///     global defaults under <see cref="AipLanguage.Name" />. A call without
    ///     <paramref name="configure" /> keeps already-registered defaults, so module enablement
    ///     never resets host configuration; a call with <paramref name="configure" /> replaces them.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection" />.</param>
    /// <param name="configure">Configures the language's global defaults.</param>
    public static IServiceCollection AddAipExpressions(
        this IServiceCollection            services,
        Action<ExpressionLanguageOptions>? configure = null
    ) {
        var options = new ExpressionLanguageOptions();
        configure?.Invoke(options);

        services.TryAddKeyedSingleton<IExpressionCompiler, AipCompiler>(AipLanguage.Name);
        services.TryAddKeyedSingleton<IExpressionPushdownPlanner, AipPushdownPlanner>(AipLanguage.Name);
        services.TryAddKeyedSingleton<IExpressionReferenceProvider, AipReferenceProvider>(AipLanguage.Name);

        if (configure is not null || !services.Any(IsDescriptor)) {
            services.RemoveAllKeyed(typeof(ExpressionLanguageDescriptor), AipLanguage.Name);
            services.AddKeyedSingleton(AipLanguage.Name, new ExpressionLanguageDescriptor(AipLanguage.Name, options.Filtering, options.MaxResidualScanRows));
        }

        return services;
    }

    private static bool IsDescriptor(ServiceDescriptor descriptor) {
        return descriptor.ServiceType == typeof(ExpressionLanguageDescriptor) && Equals(descriptor.ServiceKey, AipLanguage.Name);
    }
}
