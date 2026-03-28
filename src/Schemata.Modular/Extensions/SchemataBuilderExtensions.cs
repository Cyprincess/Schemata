using Schemata.Core;
using Schemata.Modular;
using Schemata.Modular.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Extension methods for enabling modular architecture on <see cref="SchemataBuilder" />.
/// </summary>
public static class SchemataBuilderExtensions
{
    /// <summary>
    ///     Enables modular architecture using the default runner and provider.
    /// </summary>
    /// <param name="builder">The Schemata builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static SchemataBuilder UseModular(this SchemataBuilder builder) {
        return builder.UseModular<DefaultModulesRunner, DefaultModulesProvider>();
    }

    /// <summary>
    ///     Enables modular architecture with a custom runner and the default provider.
    /// </summary>
    /// <typeparam name="TRunner">The module runner type.</typeparam>
    /// <param name="builder">The Schemata builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static SchemataBuilder UseModular<TRunner>(this SchemataBuilder builder)
        where TRunner : class, IModulesRunner {
        return builder.UseModular<TRunner, DefaultModulesProvider>();
    }

    /// <summary>
    ///     Enables modular architecture with custom runner and provider types.
    /// </summary>
    /// <typeparam name="TRunner">The module runner type.</typeparam>
    /// <typeparam name="TProvider">The module provider type.</typeparam>
    /// <param name="builder">The Schemata builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static SchemataBuilder UseModular<TRunner, TProvider>(this SchemataBuilder builder)
        where TProvider : class, IModulesProvider
        where TRunner : class, IModulesRunner {
        builder.AddFeature<SchemataModulesFeature<TProvider, TRunner>>();
        return builder;
    }
}
