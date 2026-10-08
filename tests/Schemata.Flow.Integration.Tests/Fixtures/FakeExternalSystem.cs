using System;
using System.Collections.Generic;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Integration.Tests.Fixtures;

/// <summary>
///     External-system simulator for the recovery tests: capability switches
///     (<see cref="Queryable" />, <see cref="Dedupes" />) select the recovery protocol the task handler
///     can use, <see cref="Indeterminate" /> loses the response after applying the effect, and
///     <see cref="FailNextCommitOnce" /> crashes the transition between the call and the state commit.
/// </summary>
public sealed class FakeExternalSystem
{
    private bool _failCommitOnce;

    /// <summary>Whether the system supports querying an outcome by request id.</summary>
    public bool Queryable { get; init; }

    /// <summary>Whether the receiver deduplicates repeat sends by request id.</summary>
    public bool Dedupes { get; init; }

    /// <summary>When set, <see cref="Send" /> applies the effect and then loses the response.</summary>
    public bool Indeterminate { get; set; }

    /// <summary>Send attempts observed by the receiver.</summary>
    public int Sends { get; private set; }

    /// <summary>Effective operations applied by the receiver.</summary>
    public int Applied { get; private set; }

    /// <summary>Every request id the receiver was asked about, in order (<c>query:</c>/<c>send:</c> prefixed).</summary>
    public List<string> Observed { get; } = [];

    /// <summary>Request ids whose effect landed.</summary>
    public HashSet<string> Landed { get; } = new(StringComparer.Ordinal);

    public void FailNextCommitOnce() { _failCommitOnce = true; }

    /// <summary>Outcome query: reports whether the request id's effect is known to have landed.</summary>
    public bool QueryApplied(string requestId) {
        Observed.Add($"query:{requestId}");
        return Landed.Contains(requestId);
    }

    /// <summary>Sends the effect with the request id as the receiver's idempotency key.</summary>
    public void Send(string requestId) {
        Observed.Add($"send:{requestId}");
        Sends++;
        if (Indeterminate) {
            Applied++;
            Landed.Add(requestId);
            throw new TimeoutException("The response was lost after the effect was applied.");
        }

        if (Dedupes && Landed.Contains(requestId)) {
            return;
        }

        Landed.Add(requestId);
        Applied++;
    }

    /// <summary>Arms the in-flight crash: the current transition's commit fails after the call.</summary>
    public void SimulateCrashAfterCall(FlowTaskContext context) {
        if (!_failCommitOnce) {
            return;
        }

        _failCommitOnce = false;
        context.UnitOfWork.AddSavePreparation(
            () => throw new InvalidOperationException("Simulated crash between the external call and the state commit."));
    }
}
