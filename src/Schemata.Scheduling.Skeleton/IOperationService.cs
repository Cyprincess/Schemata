using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Resource;

namespace Schemata.Scheduling.Skeleton;

/// <summary>
///     Provides in-process access to scheduler-backed long-running operations.
/// </summary>
public interface IOperationService
{
    /// <summary>Gets the current snapshot of an operation without waiting for completion.</summary>
    /// <param name="operation">The canonical or leaf operation name.</param>
    /// <param name="ct">Cancellation token for the read.</param>
    /// <returns>The current operation snapshot.</returns>
    ValueTask<Operation> GetAsync(string operation, CancellationToken ct = default);

    /// <summary>Waits until an operation reaches a terminal state.</summary>
    /// <param name="operation">The canonical or leaf operation name.</param>
    /// <param name="ct">Cancellation token controlling the wait duration.</param>
    /// <returns>The terminal operation snapshot.</returns>
    ValueTask<Operation> WaitAsync(string operation, CancellationToken ct = default);

    /// <summary>Cancels a pending operation and removes its scheduler entry when one exists.</summary>
    /// <param name="operation">The canonical or leaf operation name.</param>
    /// <param name="ct">Cancellation token for the cancellation request.</param>
    /// <returns>The terminal cancelled operation snapshot.</returns>
    ValueTask<Operation> CancelAsync(string operation, CancellationToken ct = default);

    /// <summary>Persists an already-completed operation for inline work.</summary>
    /// <param name="method">The custom method that completed the operation.</param>
    /// <param name="output">Serialized success output.</param>
    /// <param name="error">Failure message; a non-null value marks the operation as failed.</param>
    /// <param name="uid">Optional preallocated operation identifier.</param>
    /// <param name="ct">Cancellation token for the write.</param>
    /// <returns>The persisted terminal operation.</returns>
    ValueTask<Operation> CreateTerminalAsync(
        string method,
        string? output,
        string? error,
        Guid? uid = null,
        CancellationToken ct = default
    );
}
