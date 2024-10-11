using System;
using Microsoft.Extensions.Logging;

namespace Schemata.Core.Features;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class InformationAttribute : Attribute
{
    public InformationAttribute(string message, params object?[] parameters) {
        Message    = message;
        Parameters = parameters;
    }

    public string Message { get; }

    public object?[] Parameters { get; }

    public LogLevel Level { get; init; } = LogLevel.Information;
}
