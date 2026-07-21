using System;
using System.Collections.Generic;

namespace Schemata.Report.Skeleton;

/// <summary>The well-known reason codes emitted by report contracts.</summary>
public static class ReportReasons
{
    /// <summary>The supplied operation has not reached a terminal state.</summary>
    public const string OperationNotComplete = "OPERATION_NOT_COMPLETE";

    /// <summary>The supplied operation completed with an error status.</summary>
    public const string OperationFailed = "OPERATION_FAILED";

    /// <summary>The completed operation omitted or contained an invalid report output payload.</summary>
    public const string InvalidOperationOutput = "INVALID_OPERATION_OUTPUT";
}

/// <summary>A report failure carrying a reason code and optional metadata.</summary>
public sealed class ReportException : Exception
{
    /// <summary>Creates a report failure.</summary>
    /// <param name="reason">A well-known <see cref="ReportReasons" /> code.</param>
    /// <param name="message">The human-readable description.</param>
    /// <param name="metadata">Key/value pairs describing the failure context.</param>
    public ReportException(
        string                                reason,
        string                                message,
        IReadOnlyDictionary<string, string?>? metadata = null
    ) : base(message) {
        Reason   = reason;
        Metadata = metadata;
    }

    /// <summary>A <see cref="ReportReasons" /> code or a host-defined code.</summary>
    public string Reason { get; }

    /// <summary>Key/value pairs describing the failure context.</summary>
    public IReadOnlyDictionary<string, string?>? Metadata { get; }
}
