using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Messaging.Skeleton;

namespace Schemata.Scheduling.Skeleton;

/// <summary>
///     Reads the durable state of an execution by UID. The implementation is closed on the host's
///     persisted execution entity type and restores the execution's message context before reading,
///     so consumers observe cancellation without resolving the default execution repository.
/// </summary>
public interface IJobExecutionState
{
    /// <summary>Returns whether the execution identified by <paramref name="uid" /> is cancelled.</summary>
    /// <param name="uid">Execution UID to read.</param>
    /// <param name="message">Message context recorded on the execution row.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see langword="true" /> when the persisted row reached the cancelled state.</returns>
    ValueTask<bool> IsCancelledAsync(Guid uid, MessageContext message, CancellationToken ct = default);
}
