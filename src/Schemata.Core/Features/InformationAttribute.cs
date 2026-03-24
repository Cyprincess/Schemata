using System;
using Microsoft.Extensions.Logging;

namespace Schemata.Core.Features;

/// <summary>
///     Attaches a log message to a feature that is emitted when the feature is registered.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class InformationAttribute : Attribute
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="InformationAttribute" /> class.
    /// </summary>
    /// <param name="message">The log message template.</param>
    /// <param name="parameters">Optional parameters for the message template.</param>
    public InformationAttribute(string message, params object?[] parameters) {
        Message    = message;
        Parameters = parameters;
    }

    /// <summary>
    ///     Gets the log message template.
    /// </summary>
    public string Message { get; }

    /// <summary>
    ///     Gets the parameters for the message template.
    /// </summary>
    public object?[] Parameters { get; }

    /// <summary>
    ///     Gets or sets the log level for the message.
    /// </summary>
    public LogLevel Level { get; init; } = LogLevel.Information;
}
