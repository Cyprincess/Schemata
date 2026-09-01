using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Integration.Tests;

internal static class BridgeDefinitionHelpers
{
    internal static TimerDefinition Timer(string name) {
        return new() {
            Name           = name,
            TimerType      = TimerType.Duration,
            TimeExpression = "PT1M",
        };
    }
}