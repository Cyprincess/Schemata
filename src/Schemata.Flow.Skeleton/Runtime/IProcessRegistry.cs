using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>
///     Registry for process definitions and their loaded
///     <see cref="ProcessDefinition" /> AST instances.
/// </summary>
public interface IProcessRegistry
{
    /// <summary>
    ///     Registers a process definition by instantiating its
    ///     <typeparamref name="TProcess" /> type and running the
    ///     state machine validator when the
    ///     configured engine is the state machine engine.
    /// </summary>
    ValueTask RegisterAsync<TProcess>(
        string?                       engine    = null,
        Action<ProcessConfiguration>? configure = null,
        CancellationToken             ct        = default
    )
        where TProcess : ProcessDefinition;

    /// <summary>
    ///     Registers a process definition from a configuration.
    /// </summary>
    ValueTask RegisterAsync(ProcessConfiguration configuration, CancellationToken ct = default);

    /// <summary>
    ///     Registers a process definition from a serialized source.
    /// </summary>
    ValueTask RegisterAsync(
        string                        source,
        string?                       engine    = null,
        Action<ProcessConfiguration>? configure = null,
        CancellationToken             ct        = default
    );

    /// <summary>
    ///     Removes a previously registered process definition.
    /// </summary>
    ValueTask UnregisterAsync(string processName, CancellationToken ct = default);

    /// <summary>
    ///     Returns the names of all currently registered process definitions.
    /// </summary>
    IReadOnlyCollection<string> GetRegisteredProcesses();

    /// <summary>
    ///     Checks whether a process definition with the given name is registered.
    /// </summary>
    bool IsRegistered(string processName);

    /// <summary>
    ///     Retrieves the full registration for a process definition by name.
    /// </summary>
    ProcessRegistration? GetRegistration(string processName);
}
