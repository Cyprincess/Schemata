using System;
using System.Collections.Generic;

namespace Schemata.Insight.Foundation;

/// <summary>The well-known reason codes Insight reports for rejected requests.</summary>
public static class InsightReasons
{
    public const string UnknownSourceName                 = "UNKNOWN_SOURCE_NAME";
    public const string UnknownExpressionLanguage         = "UNKNOWN_EXPRESSION_LANGUAGE";
    public const string InvalidExpression                 = "INVALID_EXPRESSION";
    public const string InvalidArgument                   = "INVALID_ARGUMENT";
    public const string Unimplemented                     = "UNIMPLEMENTED";
    public const string ExpressionLanguageNotValueCapable = "EXPRESSION_LANGUAGE_NOT_VALUE_CAPABLE";
}

/// <summary>A client-facing Insight request rejection carrying a reason code and optional metadata.</summary>
public sealed class InsightValidationException : Exception
{
    /// <summary>Creates a rejection.</summary>
    /// <param name="reason">A well-known <see cref="InsightReasons" /> code.</param>
    /// <param name="message">The human-readable description.</param>
    /// <param name="metadata">Optional structured metadata (e.g. the offending name or language).</param>
    public InsightValidationException(
        string                               reason,
        string                               message,
        IReadOnlyDictionary<string, string>? metadata = null
    ) : base(message) {
        Reason   = reason;
        Metadata = metadata;
    }

    /// <summary>The reason code.</summary>
    public string Reason { get; }

    /// <summary>Optional structured metadata.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; }
}
