using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Flow.Foundation;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Runtime;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Extension methods installing the repository-backed effect recorder.</summary>
public static class SchemataFlowRepositoryServiceCollectionExtensions
{
    /// <summary>
    ///     Installs <see cref="RepositoryFlowEffectRecorder{SchemataFlowEffectIntent}" /> for the default
    ///     <see cref="SchemataFlowEffectIntent" /> entity.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemataFlowRepositoryEffectRecorder(this IServiceCollection services) {
        return services.AddSchemataFlowRepositoryEffectRecorder<SchemataFlowEffectIntent>();
    }

    /// <summary>
    ///     Installs <see cref="RepositoryFlowEffectRecorder{TEffectIntent}" /> as the <see cref="IFlowEffectRecorder" />
    ///     the flow engines record external-effect intents and completions through, over the
    ///     host-selected <typeparamref name="TEffectIntent" /> row. The recorder rides on the
    ///     application's <c>IRepository&lt;TEffectIntent&gt;</c> registration, so the persistence engine
    ///     is whichever repository provider the application installed, and every write enlists in the
    ///     flow transition's unit of work. Intent recording is framework-internal reliable execution
    ///     support: the framework does not claim exactly-once external effects, and this is not an
    ///     application-facing outbox store. Install a different <see cref="IFlowEffectRecorder" />
    ///     instead to replace the backend, or install none to keep the engines recording nothing.
    /// </summary>
    /// <typeparam name="TEffectIntent">The persisted effect-intent entity type registered by the host.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemataFlowRepositoryEffectRecorder<TEffectIntent>(this IServiceCollection services)
        where TEffectIntent : SchemataFlowEffectIntent, new() {
        services.TryAddScoped<IFlowEffectRecorder, RepositoryFlowEffectRecorder<TEffectIntent>>();
        return services;
    }
}
