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