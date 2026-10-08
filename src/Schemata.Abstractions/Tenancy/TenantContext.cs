using System;
using System.Threading;

namespace Schemata.Abstractions.Tenancy;

public static class TenantContext
{
    private static readonly AsyncLocal<Frame?> CurrentFrame = new();

    public static TenantIdentity Current => CurrentFrame.Value?.Identity ?? TenantIdentity.Host;

    /// <summary>Enter synchronously in the execution context that will invoke the operation.</summary>
    public static IDisposable Enter(TenantIdentity identity) {
        var parent = CurrentFrame.Value;
        CurrentFrame.Value = new(identity);
        return new Lease(parent);
    }

    private sealed record Frame(TenantIdentity Identity);

    private sealed class Lease(Frame? parent) : IDisposable
    {
        public void Dispose() => CurrentFrame.Value = parent;
    }
}
