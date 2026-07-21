using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Common;
using Schemata.Event.Skeleton;
using Schemata.Flow.Foundation;

namespace Schemata.Flow.Event.Internal;

/// <summary>
///     Bridges inbound events to waiting BPMN message or signal catches by invoking the engine-neutral
///     <see cref="CorrelateMessageHandler" /> / <see cref="ThrowSignalHandler" /> in
///     <c>Schemata.Flow.Foundation</c> directly.
/// </summary>
public sealed class FlowEventHandler : IEventHandler<IEvent>
{
    private readonly IEventDispatchContext _context;
    private readonly IServiceProvider      _services;

    /// <summary>Creates an event bridge that wakes matching Flow process waits via the resource-method handlers.</summary>
    public FlowEventHandler(IServiceProvider services, IEventDispatchContext context) {
        _services = services;
        _context  = context;
    }

    #region IEventHandler<IEvent> Members

    public async Task HandleAsync(IEvent @event, CancellationToken ct) {
        var subs = _context.MatchedSubscriptions;
        if (subs is null || subs.Count == 0) return;

        var signals = new HashSet<string>();
        var payload = JsonSerializer.Serialize(@event, @event.GetType(), SchemataJson.Default);
        foreach (var sub in subs) {
            if (string.IsNullOrEmpty(sub.Target)) continue;

            if (sub.CorrelationKey != null) {
                using var scope = _services.CreateScope();
                var       sp    = scope.ServiceProvider;

                var persistence = sp.GetRequiredService<ProcessPersistence>();
                var process     = await persistence.FindAsync(sp, sub.Target, ct);
                if (process is null) continue;

                var handler = sp.GetRequiredService<CorrelateMessageHandler>();
                await handler.InvokeAsync(sub.Target,
                                          new() { MessageName = sub.EventType, Payload = payload, Token = sub.Token },
                                          process, null, ct);
            } else if (signals.Add(sub.EventType)) {
                using var scope = _services.CreateScope();
                var       sp    = scope.ServiceProvider;

                var handler = sp.GetRequiredService<ThrowSignalHandler>();
                await handler.InvokeAsync(null,
                                          new() { SignalName = sub.EventType, Payload = payload, Token = null },
                                          null, null, ct);
            }
        }
    }

    #endregion
}
