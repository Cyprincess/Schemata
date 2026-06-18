using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Advice;
using Schemata.Common;
using Schemata.Event.Skeleton;
using Schemata.Event.Skeleton.Advisors;

namespace Schemata.Event.Foundation.Internal;

/// <summary>Replays outbox rows through in-process event handlers.</summary>
public sealed class InProcessEventOutboxPublisher(
    IServiceProvider                        services,
    IOptions<JsonSerializerOptions>         json,
    ILogger<InProcessEventOutboxPublisher>? logger = null
) : IEventOutboxPublisher
{
    private readonly JsonSerializerOptions _json = json.Value;

    #region IEventOutboxPublisher Members

    public async Task<EventOutboxDelivery> PublishAsync(EventOutboxMessage message, CancellationToken ct = default) {
        using var scope = services.CreateScope();

        var registry  = scope.ServiceProvider.GetRequiredService<IEventTypeRegistry>();
        var eventType = registry.Resolve(message.EventType);
        if (eventType is null) {
            throw new InvalidOperationException($"Event type '{message.EventType}' is not registered.");
        }

        var eventInstance = JsonSerializer.Deserialize(message.Payload ?? string.Empty, eventType, _json);
        if (eventInstance is not IEvent @event) {
            throw new InvalidOperationException($"Event payload for '{message.EventType}' could not be deserialized.");
        }

        var store         = scope.ServiceProvider.GetRequiredService<IEventSubscriptionStore>();
        var subscriptions = await store.FindAsync(message.EventType, ct: ct);

        var context = scope.ServiceProvider.GetRequiredService<IEventDispatchContext>();
        context.SetSubscriptions(subscriptions);

        var eventCtx = new EventContext(@event, message.EventType) {
            Payload       = message.Payload,
            CorrelationId = message.CorrelationId ?? Identifiers.NewUid().ToString("n"),
            Source        = CreateSource(message),
        };

        var observers = scope.ServiceProvider.GetServices<IEventLifecycleObserver>().ToList();
        await NotifyDeliveredAsync(observers, eventCtx, ct);

        try {
            await InvokeHandlersAsync(scope.ServiceProvider, eventType, eventInstance, ct);
            eventCtx.Result = true;
        } catch (Exception ex) {
            // An in-process replay handler failure is terminal: the consume path records the row as
            // Failed and the outbox does not retry application logic, so the exception is captured
            // rather than rethrown.
            eventCtx.Exception = ex;
        } finally {
            var consumeAdviceCtx = new AdviceContext(scope.ServiceProvider);
            switch (await Advisor.For<IEventConsumeAdvisor>()
                                 .RunAsync(consumeAdviceCtx, eventCtx, ct)) {
                case AdviseResult.Continue:
                case AdviseResult.Handle:
                case AdviseResult.Block:
                default:
                    break;
            }

            foreach (var observer in observers) {
                try {
                    await observer.OnConsumedAsync(eventCtx, ct);
                } catch (Exception ex) {
                    logger?.LogWarning(ex,
                                       "IEventLifecycleObserver.OnConsumedAsync threw for event '{EventType}'.",
                                       eventCtx.EventType);
                }
            }
        }

        // The consume path owns the terminal state (Succeeded/Failed); the dispatcher must leave it.
        return EventOutboxDelivery.Consumed;
    }

    #endregion

    private async Task NotifyDeliveredAsync(
        IReadOnlyList<IEventLifecycleObserver> observers,
        EventContext                           context,
        CancellationToken                      ct
    ) {
        foreach (var observer in observers) {
            try {
                await observer.OnDeliveredAsync(context, ct);
            } catch (Exception ex) {
                logger?.LogWarning(ex,
                                   "IEventLifecycleObserver.OnDeliveredAsync threw for event '{EventType}'.",
                                   context.EventType);
            }
        }
    }

    private static async Task InvokeHandlersAsync(
        IServiceProvider serviceProvider,
        Type             eventType,
        object           eventInstance,
        CancellationToken ct
    ) {
        var resolver      = serviceProvider.GetRequiredService<HandlerResolver>();
        var genericMethod = typeof(HandlerResolver)
                           .GetMethod(nameof(HandlerResolver.InvokeEventHandlersAsync))!
                           .MakeGenericMethod(eventType);
        var routing = serviceProvider.GetRequiredService<IOptions<SchemataEventOptions>>()
                                     .Value.RoutingTable.GetValueOrDefault(eventType, EventRouting.Broadcast);

        object? invoked;
        try {
            invoked = genericMethod.Invoke(resolver, [eventInstance, routing, ct]);
        } catch (TargetInvocationException tie) when (tie.InnerException is not null) {
            ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
            throw;
        }

        if (invoked is Task task) {
            await task;
        }
    }

    private static SourceSnapshot? CreateSource(EventOutboxMessage message) {
        if (message.SourceType is null && message.Source is null && message.SourceTimestamp is null) {
            return null;
        }

        return new() {
            SourceType    = message.SourceType,
            Source = message.Source,
            SourceTimestamp     = message.SourceTimestamp,
        };
    }

    /// <summary>
    ///     Replay-time stand-in for the originating business entity. Exposes the persisted source
    ///     fields through <see cref="ISourceReference" />, and projects them onto
    ///     <see cref="ICanonicalName" /> and <see cref="IConcurrency" /> so observers and handlers
    ///     can read the canonical name and concurrency timestamp through the same interfaces
    ///     they use on live entities.
    /// </summary>
    private sealed class SourceSnapshot : ISourceReference, ICanonicalName, IConcurrency
    {
        #region ICanonicalName Members

        public string? Name { get; set; }

        public string? CanonicalName {
            get => Source;
            set => Source = value;
        }

        #endregion

        #region IConcurrency Members

        public Guid Timestamp {
            get => SourceTimestamp ?? Guid.Empty;
            set => SourceTimestamp = value;
        }

        #endregion

        #region ISourceReference Members

        public string? SourceType { get; set; }

        public string? Source { get; set; }

        public Guid? SourceTimestamp { get; set; }

        #endregion
    }
}
