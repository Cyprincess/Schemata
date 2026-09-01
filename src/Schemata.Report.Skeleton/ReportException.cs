using System;
using System.Collections.Generic;

namespace Schemata.Report.Skeleton;

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
