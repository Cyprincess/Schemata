using System;
using Microsoft.Extensions.Logging;

namespace Schemata.Core.Features;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class InformationAttribute(string message) : Attribute
{
    public string Message { get; } = message;

    public LogLevel Level { get; init; } = LogLevel.Information;
}
