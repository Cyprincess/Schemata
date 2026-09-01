using System.Threading;

namespace Schemata.Flow.Integration.Tests.Fixtures;

public static class AmbientOwner
{
    public static readonly AsyncLocal<string?> Current = new();
}