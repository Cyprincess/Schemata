using System;
using System.Collections.Generic;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Insight.Foundation.Planning;

/// <summary>A client-facing Insight request rejection carrying a reason code and optional metadata.</summary>
public sealed class InsightValidationException : SchemataException
{
    /// <summary>Creates a rejection.</summary>
    /// <param name="reason">A well-known <see cref="InsightReasons" /> code.</param>
    /// <param name="resourceKey">The message template in SchemataResources.</param>
    /// <param name="metadata">Optional structured metadata (e.g. the offending name or language).</param>
    /// <param name="innerException">Diagnostic cause excluded from the public response envelope.</param>
    public InsightValidationException(
        string                                reason,
        string                                resourceKey,
        IReadOnlyDictionary<string, string?>? metadata = null,
        Exception?                            innerException = null
    ) : base(CodeFor(reason), StatusFor(reason), LocalizedMessageFormatter.FormatInvariant(resourceKey, metadata), innerException) {
        Reason = reason;
        MessageResourceKey = resourceKey;
        Details = [new ErrorInfoDetail { Reason = reason }];
        AttachMetadata(metadata);
        Metadata = ((ErrorInfoDetail)Details[0]).Metadata;
    }

    /// <summary>The reason code.</summary>
    public string Reason { get; }

    /// <summary>Optional structured metadata.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; }

    protected override string? MessageResourceKey { get; }
    public override string? Domain => "schemata.insight";

    private static int CodeFor(string reason) => reason switch {
        InsightReasons.UnknownSourceName => 404,
        InsightReasons.Unimplemented => 501,
        _ => 400,
    };

    private static string StatusFor(string reason) => reason switch {
        InsightReasons.UnknownSourceName => ErrorCodes.NotFound,
        InsightReasons.Unimplemented => ErrorCodes.Unimplemented,
        _ => ErrorCodes.InvalidArgument,
    };
}
