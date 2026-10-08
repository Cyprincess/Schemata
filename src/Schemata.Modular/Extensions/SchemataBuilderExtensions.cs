using System;
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
    ///     Enables modular architecture with a custom runner type and the default provider.
    /// </summary>
    /// <typeparam name="TRunner">The module runner type.</typeparam>
    /// <param name="builder">The Schemata builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static SchemataBuilder UseModular<TRunner>(this SchemataBuilder builder)
        where TRunner : class, IModulesRunner {
        return builder.UseModular<TRunner, DefaultModulesProvider>();
    }

    /// <summary>
    ///     Enables modular architecture with an explicit runner instance and the default
    ///     provider: the supplied instance is used directly for every lifecycle phase. Repeating
    ///     the call with the same instance is idempotent; naming a different instance of the
    ///     same type is rejected here, before the feature runs.
    /// </summary>
    /// <typeparam name="TRunner">The module runner type.</typeparam>
    /// <param name="builder">The Schemata builder.</param>
    /// <param name="runner">The runner instance that owns every phase.</param>
    /// <returns>The builder for chaining.</returns>
    public static SchemataBuilder UseModular<TRunner>(this SchemataBuilder builder, TRunner runner)
        where TRunner : class, IModulesRunner {
        return builder.UseModular<TRunner, DefaultModulesProvider>(runner);
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

    /// <summary>
    ///     Enables modular architecture with custom runner and provider types and an explicit
    ///     runner instance: the supplied instance is used directly for every lifecycle phase.
    ///     Repeating the call with the same instance is idempotent; naming a different instance
    ///     of the same type is rejected here, before the feature runs.
    /// </summary>
    /// <typeparam name="TRunner">The module runner type.</typeparam>
    /// <typeparam name="TProvider">The module provider type.</typeparam>
    /// <param name="builder">The Schemata builder.</param>
    /// <param name="runner">The runner instance that owns every phase.</param>
    /// <returns>The builder for chaining.</returns>
    public static SchemataBuilder UseModular<TRunner, TProvider>(this SchemataBuilder builder, TRunner runner)
        where TProvider : class, IModulesProvider
        where TRunner : class, IModulesRunner {
        var pending = builder.Options.Get<IModulesRunner>(ModularConstants.PendingRunnerKey);
        if (pending is not null && !ReferenceEquals(pending, runner)) {
            throw new InvalidOperationException(
                $"Schemata Modular supports one module runner instance per host; a different "
              + $"'{pending.GetType().FullName}' instance is already selected.");
        }

        builder.Options.Set(ModularConstants.PendingRunnerKey, runner);
        return builder.UseModular<TRunner, TProvider>();
    }
}
