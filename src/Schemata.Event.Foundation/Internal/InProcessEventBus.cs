using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Advice;
using Schemata.Common;
using Schemata.Event.Skeleton;
using Schemata.Event.Skeleton.Advisors;

namespace Schemata.Event.Foundation.Internal;

/// <summary>Single-process <see cref="IEventBus"/> dispatching through the event outbox.</summary>
public sealed class InProcessEventBus : IEventBus
{
    private readonly EventOutboxDispatcher?      _dispatcher;
    private readonly JsonSerializerOptions       _json;
    private readonly ILogger<InProcessEventBus>? _logger;
    private readonly IServiceProvider            _services;

    /// <summary>Initializes an in-process event bus using scoped handlers and lifecycle observers.</summary>
    public InProcessEventBus(
        IServiceProvider             services,
        IOptions<JsonSerializerOptions> json,
        ILogger<InProcessEventBus>?  logger     = null,
        EventOutboxDispatcher?       dispatcher = null
    ) {
        _services   = services;
        _json       = json.Value;
        _logger     = logger;
        _dispatcher = dispatcher;
    }

    #region IEventBus Members

    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : IEvent {
        return PublishCoreAsync(@event, null, ct);
    }

    public async Task PublishAsync<TEvent>(TEvent @event, object sourceEntity, CancellationToken ct = default)
        where TEvent : IEvent {
        EventSourceContract.Ensure(sourceEntity);
        await PublishCoreAsync(@event, sourceEntity, ct);
    }

    public async Task<TResponse> SendAsync<TRequest, TResponse>(TRequest request, CancellationToken ct = default)
        where TRequest : IRequest<TResponse> {
        using var scope    = _services.CreateScope();
        var       resolver = scope.ServiceProvider.GetRequiredService<HandlerResolver>();
        var       registry = scope.ServiceProvider.GetRequiredService<IEventTypeRegistry>();

        // Same RegisterEvent contract as PublishAsync: the wire name is mandatory so the
        // audit record and any handler-observed EventContext.EventType match the registry.
        var name = registry.RequireName(typeof(TRequest));

        var eventCtx = new EventContext(request, name) {
            Payload       = JsonSerializer.Serialize(request, _json),
            CorrelationId = Identifiers.NewUid().ToString("n"),
        };
        var adviceCtx = new AdviceContext(scope.ServiceProvider);

        switch (await Advisor.For<IEventPublishAdvisor>()
                             .RunAsync(adviceCtx, eventCtx, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when adviceCtx.TryGet<TResponse>(out var r) && r is not null:
                eventCtx.Result = r;
                return r;
            case AdviseResult.Block:
            default:
                throw new InvalidOperationException("Request blocked by advisor.");
        }

        var observers = scope.ServiceProvider.GetServices<IEventLifecycleObserver>().ToList();
        await NotifyPublishedAsync(observers, eventCtx, ct);

        try {
            var result = await resolver.InvokeRequestHandlerAsync<TRequest, TResponse>(request, ct);
            eventCtx.Result = result;
            return result;
        } catch (Exception ex) {
            eventCtx.Exception = ex;
            throw;
        } finally {
            // PublishAsync uses the same observational consume-advisor contract; the
            // handler has either returned or thrown, so all three advice results converge.
            var consumeAdviceCtx = new AdviceContext(scope.ServiceProvider);
            switch (await Advisor.For<IEventConsumeAdvisor>()
                                 .RunAsync(consumeAdviceCtx, eventCtx, ct)) {
                case AdviseResult.Continue:
                case AdviseResult.Handle:
                case AdviseResult.Block:
                default:
                    break;
            }
            await NotifyConsumedAsync(observers, eventCtx, ct);
        }
    }

    #endregion

    private async Task PublishCoreAsync<TEvent>(TEvent @event, object? source, CancellationToken ct)
        where TEvent : IEvent {
        using var scope    = _services.CreateScope();
        var       registry = scope.ServiceProvider.GetRequiredService<IEventTypeRegistry>();

        // Resolve by the runtime type so a derived event published through a base/interface
        // static type keeps its registered name and serializes its derived members.
        var type = @event.GetType();
        var name = registry.RequireName(type);

        var ctx = new EventContext(@event, name) {
            Payload                = JsonSerializer.Serialize(@event, type, _json),
            CorrelationId          = Identifiers.NewUid().ToString("n"),
            RequiresOutboxDelivery = true,
            Source                 = source,
        };
        var adviceCtx = new AdviceContext(scope.ServiceProvider);

        switch (await Advisor.For<IEventPublishAdvisor>()
                             .RunAsync(adviceCtx, ctx, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when adviceCtx.TryGet<object>(out var r):
                ctx.Result = r;
                return;
            case AdviseResult.Block:
            default:
                throw new InvalidOperationException("Event publish blocked by advisor.");
        }

        var observers = scope.ServiceProvider.GetServices<IEventLifecycleObserver>().ToList();
        await NotifyPublishedAsync(observers, ctx, ct);
        _dispatcher?.NotifyPending();
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

    private async Task NotifyConsumedAsync(
        IReadOnlyList<IEventLifecycleObserver> observers,
        EventContext                           context,
        CancellationToken                      ct
    ) {
        foreach (var observer in observers) {
            try {
                await observer.OnConsumedAsync(context, ct);
            } catch (Exception ex) {
                _logger?.LogWarning(ex, "IEventLifecycleObserver.OnConsumedAsync threw for event '{EventType}'.",
                                    context.EventType);
            }
        }
    }
}
