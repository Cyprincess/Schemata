using System.Collections.Generic;
using Schemata.Abstractions;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Foundation.Builders;

/// <summary>
///     Provides a builder for configuring process flows.
/// </summary>
public sealed class FlowBuilder
{
    private readonly List<ProcessConfiguration> _configurations = new();

    /// <summary>
    ///     Registers a code-first process definition type.
    /// </summary>
    /// <typeparam name="TProcess">The process definition type.</typeparam>
    /// <param name="engine">The engine name. Defaults to BPMN.</param>
    /// <returns>The process configuration for further configuration.</returns>
    public ProcessConfiguration Use<TProcess>(string? engine = null)
        where TProcess : ProcessDefinition {
        var config = new ProcessConfiguration {
            Name           = typeof(TProcess).Name,
            Engine         = engine ?? SchemataConstants.FlowEngines.StateMachine,
            DefinitionType = typeof(TProcess),
        };
        _configurations.Add(config);
        return config;
    }

    /// <summary>
    ///     Registers a code-first process definition type with a single entity type.
    ///     Automatically selects the StateMachine engine.
    /// </summary>
    /// <typeparam name="TProcess">The process definition type.</typeparam>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="engine">The engine name. Defaults to StateMachine.</param>
    /// <returns>The process configuration for further configuration.</returns>
    public ProcessConfiguration Use<TProcess, TEntity>(string? engine = null)
        where TProcess : ProcessDefinition {
        var config = new ProcessConfiguration {
            Name       = typeof(TProcess).Name,
            Engine     = engine ?? SchemataConstants.FlowEngines.StateMachine,
            EntityType = typeof(TEntity),
        };
        _configurations.Add(config);
        return config;
    }

    /// <summary>
    ///     Builds the list of process configurations.
    /// </summary>
    /// <returns>A read-only list of process configurations.</returns>
    public IReadOnlyList<ProcessConfiguration> Build() { return _configurations; }
}
