using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Expressions.Skeleton;

namespace Schemata.Expressions.Order;

/// <summary>
///     Order expression registration extensions.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the language-independent AIP-132 order compiler as a single non-keyed service.
    /// </summary>
    public static IServiceCollection AddOrderExpressions(this IServiceCollection services) {
        services.TryAddSingleton<IOrderCompiler, OrderCompiler>();
        return services;
    }

    /// <summary>
    ///     Registers the language-independent order compiler on a module builder.
    /// </summary>
    /// <typeparam name="T">The module builder type.</typeparam>
    /// <param name="builder">The module builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static T UseOrdering<T>(this T builder)
        where T : IExpressionLanguageBuilder {
        builder.Services.AddOrderExpressions();
        return builder;
    }
}
