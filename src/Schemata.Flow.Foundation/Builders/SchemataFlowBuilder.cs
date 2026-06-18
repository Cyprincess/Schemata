using System;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Foundation.Builders;

/// <summary>
///     Fluent builder for configuring the flow system. Process definitions registered through
///     <c>Use&lt;TProcess&gt;</c> are written directly to <see cref="SchemataFlowOptions" />.
/// </summary>
public sealed class SchemataFlowBuilder
{
    /// <summary>Initializes the builder bound to the given options and service collection.</summary>
    /// <param name="schemata">The <see cref="SchemataOptions" />.</param>
    /// <param name="services">The service collection that receives process registrations.</param>
    public SchemataFlowBuilder(SchemataOptions schemata, IServiceCollection services) {
        Schemata = schemata;
        Services = services;
    }

    private SchemataOptions Schemata { get; }

    /// <summary>Service collection that receives process registrations.</summary>
    public IServiceCollection Services { get; }

    /// <summary>
    ///     Adds a feature to the Schemata configuration.
    /// </summary>
    /// <typeparam name="T">The <see cref="ISimpleFeature" /> type.</typeparam>
    public void AddFeature<T>()
        where T : ISimpleFeature {
        Schemata.AddFeature<T>();
    }

    /// <summary>
    ///     Registers a code-first process definition type, writing its
    ///     <see cref="ProcessConfiguration" /> to <see cref="SchemataFlowOptions" />. Returns this
    ///     builder so multiple definitions chain; configure the definition through
    ///     <paramref name="configure" />.
    /// </summary>
    /// <typeparam name="TProcess">The process definition type.</typeparam>
    /// <param name="configure">An optional callback to configure the process definition.</param>
    /// <returns>This builder for chaining.</returns>
    public SchemataFlowBuilder Use<TProcess>(Action<ProcessConfiguration>? configure = null)
        where TProcess : ProcessDefinition {
        return Use<TProcess>(null, configure);
    }

    /// <summary>
    ///     Registers a code-first process definition type against a specific engine, writing its
    ///     <see cref="ProcessConfiguration" /> to <see cref="SchemataFlowOptions" />.
    /// </summary>
    /// <typeparam name="TProcess">The process definition type.</typeparam>
    /// <param name="engine">The engine name; <see langword="null" /> uses the state-machine engine.</param>
    /// <param name="configure">An optional callback to configure the process definition.</param>
    /// <returns>This builder for chaining.</returns>
    public SchemataFlowBuilder Use<TProcess>(string? engine, Action<ProcessConfiguration>? configure = null)
        where TProcess : ProcessDefinition {
        var config = new ProcessConfiguration {
            Name           = typeof(TProcess).Name,
            Engine         = engine ?? SchemataConstants.FlowEngines.StateMachine,
            DefinitionType = typeof(TProcess),
        };

        configure?.Invoke(config);

        Services.Configure<SchemataFlowOptions>(options => options.Configurations.Add(config));

        return this;
    }
}
