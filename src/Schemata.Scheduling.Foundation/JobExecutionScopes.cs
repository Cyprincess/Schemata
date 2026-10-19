using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Messaging.Skeleton;

namespace Schemata.Scheduling.Foundation;

/// <summary>Creates the fresh DI scope that owns one lease transition.</summary>
internal delegate ValueTask<JobExecutionScope> JobExecutionScopeFactory(CancellationToken ct);

/// <summary>
///     A prepared message scope and the persisted context it restores. A null context marks a
///     scope created from the caller's own container, which restores no captured context.
/// </summary>
internal sealed record JobExecutionScope(MessageExecutionScope Scope, MessageContext? Context);

internal static class JobExecutionScopes
{
    /// <summary>
    ///     Creates scopes from the caller's container. The container decides which tenant the
    ///     scope belongs to; the persisted execution message context is not consulted.
    /// </summary>
    public static JobExecutionScopeFactory Plain(IServiceScopeFactory scopes) => ct => {
        ct.ThrowIfCancellationRequested();
        var scope = new AsyncServiceScope(scopes.CreateScope());

        return ValueTask.FromResult(new JobExecutionScope(new(scope, static () => NoopLease.Instance), null));
    };

    /// <summary>Creates message scopes that enter and restore the persisted execution context.</summary>
    public static JobExecutionScopeFactory Messaging(IMessageExecutionScopeFactory scopes, MessageContext context) => async ct =>
        new(await scopes.CreateAsync(context, ct), context);

    private sealed class NoopLease : IDisposable
    {
        public static readonly NoopLease Instance = new();

        public void Dispose() { }
    }
}
