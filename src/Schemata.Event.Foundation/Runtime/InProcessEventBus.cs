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
using Schemata.Advice;
using Schemata.Entity.Repository;
using Schemata.Event.Foundation.Observers;
using Schemata.Event.Skeleton;
using Schemata.Event.Skeleton.Advisors;
using Schemata.Event.Skeleton.Entities;
using Schemata.Messaging.Skeleton;

namespace Schemata.Event.Foundation.Runtime;

/// <summary>
///     Single-process <see cref="IEventBus" /> that dispatches each event to its in-process
///     handlers within the publish call.
/// </summary>
public sealed class InProcessEventBus : IEventBus
{
    private readonly JsonSerializerOptions       _json;
    private readonly ILogger<InProcessEventBus>? _logger;
    private readonly IServiceProvider            _services;

    /// <summary>Initializes an in-process event bus using scoped handlers and lifecycle observers.</summary>
    public InProcessEventBus(
        IServiceProvider                services,
        IOptions<JsonSerializerOptions> json,
        ILogger<InProcessEventBus>?     logger = null
    ) {
        _services = services;
        _json     = json.Value;
        _logger   = logger;
    }

    #region IEventBus Members

    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : IEvent {
        return PublishCoreAsync(@event, null, null, ct);
    }

    public Task PublishAsync<TEvent>(TEvent @event, IReadOnlyDictionary<string, string> correlation, CancellationToken ct = default)
        where TEvent : IEvent {
        return PublishCoreAsync(@event, null, correlation, ct);
    }

    public async Task PublishAsync<TEvent>(TEvent @event, object sourceEntity, CancellationToken ct = default)
        where TEvent : IEvent {
        EventSourceContract.Ensure(sourceEntity);
        await PublishCoreAsync(@event, sourceEntity, null, ct);
    }

    public async Task PublishAsync<TEvent>(
        TEvent                              @event,
        object                              sourceEntity,
        IReadOnlyDictionary<string, string> correlation,
        CancellationToken                   ct = default
    )
        where TEvent : IEvent {
        EventSourceContract.Ensure(sourceEntity);
        await PublishCoreAsync(@event, sourceEntity, correlation, ct);
    }

    #endregion

    private async Task PublishCoreAsync<TEvent>(
        TEvent                               @event,
        object?                              source,
        IReadOnlyDictionary<string, string>? correlation,
        CancellationToken                    ct
    )
        where TEvent : IEvent {
        var message = MessageContexts.Capture(_services);
        var scope = await _services.GetRequiredService<IMessageExecutionScopeFactory>().CreateAsync(message, ct);
        using var identity = scope.Enter();
        await using var owned = scope;
        await scope.RestoreAsync(message, ct);
        var       registry = scope.Services.GetRequiredService<IEventTypeRegistry>();

        // Resolve by the runtime type so a derived event published through a base/interface
        // static type keeps its registered name and serializes its derived members.
        var type = @event.GetType();
        var name = registry.RequireName(type);

        var ctx = new EventContext(@event, name) {
            Payload       = JsonSerializer.Serialize(@event, type, _json),
            CorrelationId = Guid.NewGuid().ToString("n"),
            Correlation   = correlation,
            Source        = source,
        };
        var adviceCtx = new AdviceContext(scope.Services);
        using var publishScope = AdviceContext.Establish(adviceCtx);

        switch (await Advisor.For<IEventPublishAdvisor>()
                             .RunAsync(adviceCtx, ctx, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle:
                return;
            case AdviseResult.Block:
            default:
                throw new InvalidOperationException("Event publish blocked by advisor.");
        }

        var observers = scope.Services.GetServices<IEventLifecycleObserver>().ToList();
        await NotifyPublishedAsync(observers, ctx, ct);

        var subscriptions = scope.Services.GetRequiredService<IRepository<SchemataEventSubscription>>();
        var matched       = new List<SchemataEventSubscription>();
        await foreach (var sub in subscriptions.ListMatchingAsync(name, ctx.Correlation, ct)) {
            matched.Add(sub);
        }

        try {
            // A publish with no subscribers succeeds silently; the consume callbacks are skipped.
            if (!await DispatchHandlersAsync(scope.Services, type, @event, matched, ct)) {
                return;
            }

            ctx.Result = true;
        } catch (Exception ex) {
            // Rethrown below so the audit observer records the failure first.
            ctx.Exception = ex;
        }

        var consumeAdviceCtx = new AdviceContext(scope.Services);
        using var consumeScope = AdviceContext.Establish(consumeAdviceCtx);
        switch (await Advisor.For<IEventConsumeAdvisor>()
                             .RunAsync(consumeAdviceCtx, ctx, ct)) {
            case AdviseResult.Continue:
            case AdviseResult.Handle:
            case AdviseResult.Block:
            default:
                break;
        }

        // The audit observer runs last so the audit row reflects the application observers' outcome;
        // the first failure is captured, the remaining observers still run, and the failure escapes below.
        var consumeObservers = observers.OrderBy(observer => observer is SchemataEventAuditObserver)
                                        .ToList();
        Exception? observerFailure = null;
        foreach (var observer in consumeObservers) {
            try {
                await observer.OnConsumedAsync(ctx, ct);
            } catch (Exception ex) {
                if (observerFailure is null) {
                    observerFailure = ex;
                    ctx.Exception   = ex;
                }
            }
        }

        if (observerFailure is not null) {
            ExceptionDispatchInfo.Capture(observerFailure).Throw();
        }

        if (ctx.Exception is not null) {
            ExceptionDispatchInfo.Capture(ctx.Exception).Throw();
        }
    }

    private async Task NotifyPublishedAsync(
        IReadOnlyList<IEventLifecycleObserver> observers,
        EventContext                           context,
        CancellationToken                      ct
    ) {
        foreach (var observer in observers) {
            try {
                await observer.OnPublishedAsync(context, ct);
            } catch (Exception ex) {
                _logger?.LogWarning(ex, "IEventLifecycleObserver.OnPublishedAsync threw for event '{EventType}'.",
                                    context.EventType);
            }
        }
    }

    // The runtime event type is known only at publish time, so dispatch crosses into generics once
    // through this wrapper; the candidate probe and the invocation share one resolution.
    private static readonly MethodInfo InvokeResolved = typeof(InProcessEventBus).GetMethod(
        nameof(InvokeResolvedHandlersAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static async Task<bool> DispatchHandlersAsync(
        IServiceProvider                         serviceProvider,
        Type                                     eventType,
        object                                   eventInstance,
        IReadOnlyList<SchemataEventSubscription> subscriptions,
        CancellationToken                        ct
    ) {
        object? invoked;
        try {
            invoked = InvokeResolved.MakeGenericMethod(eventType)
                                    .Invoke(null, [serviceProvider, eventInstance, subscriptions, ct]);
        } catch (TargetInvocationException tie) when (tie.InnerException is not null) {
            ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
            throw;
        }

        return invoked is Task<bool> delivered && await delivered;
    }

    private static async Task<bool> InvokeResolvedHandlersAsync<TEvent>(
        IServiceProvider                         serviceProvider,
        TEvent                                   @event,
        IReadOnlyList<SchemataEventSubscription> subscriptions,
        CancellationToken                        ct
    )
        where TEvent : IEvent {
        var resolver   = new HandlerResolver(serviceProvider);
        var candidates = resolver.ResolveHandlers<TEvent>();
        if (candidates.Count == 0) {
            return false;
        }

        serviceProvider.GetRequiredService<IEventDispatchContext>().SetSubscriptions(subscriptions);
        var routing = serviceProvider.GetRequiredService<IEventTypeRegistry>().GetRouting(typeof(TEvent));
        await resolver.InvokeEventHandlersAsync(@event, candidates, routing, ct);
        return true;
    }
}
