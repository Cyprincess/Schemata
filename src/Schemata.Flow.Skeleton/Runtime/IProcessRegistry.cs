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
    ///     Removes one exact registered version.
    /// </summary>
    ValueTask UnregisterAsync(string processName, string version, CancellationToken ct = default);

    /// <summary>
    ///     Returns all registered definition versions.
    /// </summary>
    IReadOnlyCollection<ProcessRegistration> GetRegisteredProcesses();

    /// <summary>
    ///     Checks whether the selected version is registered.
    /// </summary>
    bool IsRegistered(string processName, string version = "1");

    /// <summary>
    ///     Resolves an exact version, or the explicit "latest" selection for a new start.
    /// </summary>
    ProcessRegistration? GetRegistration(string processName, string version = "1");
}
