using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Common;
using Schemata.Event.Skeleton;
using Schemata.Flow.Foundation;
using Schemata.Flow.Foundation.Commands;
using Schemata.Flow.Skeleton.Models;
using Schemata.Messaging.Skeleton;
using CorrelateProcessRequest = Schemata.Flow.Foundation.Commands.CorrelateMessageRequest;
using ThrowProcessSignalRequest = Schemata.Flow.Foundation.Commands.ThrowSignalRequest;

namespace Schemata.Flow.Event.Internal;

/// <summary>
///     Bridges inbound events to waiting BPMN message or signal catches through the unkeyed Flow
///     request handlers.
/// </summary>
public sealed class FlowEventHandler : IEventHandler<IEvent>
{
    private readonly IEventDispatchContext _context;
    private readonly IServiceProvider      _services;

    /// <summary>Creates an event bridge that wakes matching Flow process waits through request handlers.</summary>
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

                var dispatcher = sp.GetRequiredService<IRequestDispatcher>();
                await dispatcher.SendAsync<CorrelateProcessRequest, ProcessSnapshot>(
                    new(sub.Target, sub.EventType, payload, sub.Token, Principal: null), ct);
            } else if (signals.Add(sub.EventType)) {
                using var scope = _services.CreateScope();
                var       sp    = scope.ServiceProvider;

                var dispatcher = sp.GetRequiredService<IRequestDispatcher>();
                await dispatcher.SendAsync<ThrowProcessSignalRequest, IReadOnlyList<SignalDeliveryResult>>(
                    new(sub.EventType, payload, Token: null, Principal: null), ct);
            }
        }
    }

    #endregion
}
