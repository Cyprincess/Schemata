using System;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     The one execution payload for an identified resource dispatch. The idempotency wrap creates
///     it when the reservation is reserved and fills its reservation side; the mutation owner that
///     actually stages the dispatch fills its completion side, either directly (standard verbs own
///     the unit of work) or through an explicit report (AIP-136 custom methods). The payload
///     travels on the dispatch object the owner already receives: the envelope for standard verbs,
///     the inner request for custom methods.
/// </summary>
public sealed class ResourceDispatchExecution
{
    /// <summary>
    ///     The cache key of the reservation. Filled by the wrap before the dispatch runs.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    ///     The serialized reservation bytes this attempt wrote, used as the compare-and-swap
    ///     expectation for every settlement operation. Filled by the wrap before the dispatch runs.
    /// </summary>
    public byte[] Reservation { get; set; } = [];

    /// <summary>
    ///     The request payload hash binding the reservation to one request body.
    /// </summary>
    public string PayloadHash { get; set; } = string.Empty;

    /// <summary>
    ///     The retention applied to reservation and finalized entries alike.
    /// </summary>
    public TimeSpan Retention { get; set; }

    /// <summary>
    ///     The completion evidence reported so far. Terminal states
    ///     (<see cref="ResourceDispatchOutcome.NotWritten"/>, <c>Committed</c>,
    ///     <c>RolledBack</c>) never change once reported; the mark methods are the only
    ///     writers.
    /// </summary>
    public ResourceDispatchOutcome Outcome { get; internal set; } = ResourceDispatchOutcome.None;

    /// <summary>
    ///     Reports that the dispatch reached a mutation owner. Terminal outcomes win over this
    ///     report regardless of ordering.
    /// </summary>
    public void MarkReached() {
        if (Outcome == ResourceDispatchOutcome.None) {
            Outcome = ResourceDispatchOutcome.Reached;
        }
    }

    /// <summary>
    ///     Reports that the current transaction completed without staging a write.
    /// </summary>
    public void MarkNotWritten() {
        if (Outcome is ResourceDispatchOutcome.None or ResourceDispatchOutcome.Reached) {
            Outcome = ResourceDispatchOutcome.NotWritten;
        }
    }

    /// <summary>
    ///     Reports that the provider transaction completed. Reports after a terminal outcome keep
    ///     that outcome; the first terminal report stands.
    /// </summary>
    public void MarkCommitted() {
        if (Outcome is ResourceDispatchOutcome.None or ResourceDispatchOutcome.Reached) {
            Outcome = ResourceDispatchOutcome.Committed;
        }
    }

    /// <summary>
    ///     Reports that the unit of work rolled back (or was disposed) without a commit.
    /// </summary>
    public void MarkRolledBack() {
        if (Outcome is ResourceDispatchOutcome.None or ResourceDispatchOutcome.Reached) {
            Outcome = ResourceDispatchOutcome.RolledBack;
        }
    }
}
