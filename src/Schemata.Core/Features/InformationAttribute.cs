using System;
using Microsoft.Extensions.Logging;

namespace Schemata.Core.Features;

/// <summary>
///     Emits a log message when the decorated feature is registered. Takes effect
///     only when <see cref="SchemataLoggingFeature" /> is present.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class InformationAttribute : Attribute
{
    /// <param name="message">Log message template.</param>
    /// <param name="parameters">Optional template arguments.</param>
    public InformationAttribute(string message, params object?[] parameters) {
        Message    = message;
        Parameters = parameters;
    }

    /// <summary>
    ///     The log message template string.
    /// </summary>
    public string Message { get; }

    /// <summary>
    ///     Template arguments injected into <see cref="Message" />.
    /// </summary>
    public object?[] Parameters { get; }

    /// <summary>
    ///     Log level to use.
    /// </summary>
    public LogLevel Level { get; init; } = LogLevel.Information;
}
