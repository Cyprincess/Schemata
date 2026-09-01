using System;
using System.Collections.Generic;

namespace Schemata.Insight.Foundation.Planning;

/// <summary>A client-facing Insight request rejection carrying a reason code and optional metadata.</summary>
public sealed class InsightValidationException : Exception
{
    /// <summary>Creates a rejection.</summary>
    /// <param name="reason">A well-known <see cref="InsightReasons" /> code.</param>
    /// <param name="message">The human-readable description.</param>
    /// <param name="metadata">Optional structured metadata (e.g. the offending name or language).</param>
    public InsightValidationException(
        string                                reason,
        string                                message,
        IReadOnlyDictionary<string, string?>? metadata = null
    ) : base(message) {
        Reason   = reason;
        Metadata = metadata;
    }

    /// <summary>The reason code.</summary>
    public string Reason { get; }

    /// <summary>Optional structured metadata.</summary>
    public IReadOnlyDictionary<string, string?>? Metadata { get; }
}
