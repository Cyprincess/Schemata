using System;

namespace Schemata.Flow.Foundation;

/// <summary>Outcome of delivering a broadcast signal to one waiting process.</summary>
public enum SignalDeliveryStatus
{
    /// <summary>The signal reached a target and the resulting snapshot was committed.</summary>
    Delivered,

    /// <summary>The process had no token waiting on the signal by the time its delivery ran.</summary>
    NoLongerWaiting,

    /// <summary>Delivery threw; <see cref="SignalDeliveryResult.Error" /> carries the exception.</summary>
    Failed,

    /// <summary>Delivery was cancelled before it completed.</summary>
    Canceled,
}

/// <summary>
///     Per-target outcome of a signal broadcast. A broadcast commits each target independently, so
///     the caller reads this list to learn which targets landed and which did not.
/// </summary>
/// <param name="ProcessCanonicalName">Canonical name of the process the delivery was addressed to.</param>
/// <param name="Status">How the delivery ended.</param>
/// <param name="Error">The failure, when <paramref name="Status" /> is <see cref="SignalDeliveryStatus.Failed" />.</param>
public sealed record SignalDeliveryResult(
    string               ProcessCanonicalName,
    SignalDeliveryStatus Status,
    Exception?           Error = null);
