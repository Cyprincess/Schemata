using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Event.Skeleton;
using Schemata.Flow.Foundation;
using Schemata.Flow.Skeleton.Models;
using Schemata.Messaging.Skeleton;
using CorrelateProcessRequest = Schemata.Flow.Foundation.Commands.CorrelateMessageRequest;
using ThrowProcessSignalRequest = Schemata.Flow.Foundation.Commands.ThrowSignalRequest;

namespace Schemata.Flow.Event.Handlers;

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
        // The consumer side already materialized the event, so hand the instance itself to the flow
        // handlers: a serialized JSON string would fail payload binding for processes that declare no
        // payload type for the catch.
        object payload = @event;
        foreach (var sub in subs) {
            if (string.IsNullOrEmpty(sub.Target)) continue;

            if (sub.CorrelationKey != null) {
                using var scope = _services.CreateScope();
                var       sp    = scope.ServiceProvider;

                var dispatcher = sp.GetRequiredService<IRequestDispatcher>();
                await dispatcher.SendAsync<CorrelateProcessRequest, ProcessSnapshot>(
                    new(sub.Target, sub.EventType, payload, sub.Token, Principal: FlowSystemPrincipal.Instance), ct);
            } else if (signals.Add(sub.EventType)) {
                using var scope = _services.CreateScope();
                var       sp    = scope.ServiceProvider;

                var dispatcher = sp.GetRequiredService<IRequestDispatcher>();
                var results = await dispatcher.SendAsync<ThrowProcessSignalRequest, IReadOnlyList<SignalDeliveryResult>>(
                    new(sub.EventType, payload, Token: null, Principal: FlowSystemPrincipal.Instance), ct);

                // A broadcast reports per-target outcomes instead of throwing. The bridge must not
                // let a faulted delivery look like a successful publish, so the first actual fault
                // is rethrown with its original error identity; NoLongerWaiting is a legitimate
                // race outcome and stays non-fatal.
                var failure = results.FirstOrDefault(result => result.Status == SignalDeliveryStatus.Failed);
                if (failure?.Error is { } error) {
                    ExceptionDispatchInfo.Capture(error).Throw();
                }
            }
        }
    }

    #endregion
}
