using System;

namespace Schemata.Messaging.Skeleton.Tests.Fixtures;

/// <summary>An empty provider for advisor-unit tests that never resolve a service.</summary>
public sealed class EmptyServiceProvider : IServiceProvider
{
    public object? GetService(Type serviceType) => null;
}