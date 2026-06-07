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
using Schemata.Event.Skeleton;
using Schemata.Event.Skeleton.Advisors;

namespace Schemata.Event.Foundation.Internal;

/// <summary>Single-process <see cref="IEventBus"/> dispatching directly through DI-resolved handlers.</summary>
public sealed class InProcessEventBus : IEventBus
{
    private readonly JsonSerializerOptions       _json;
    private readonly ILogger<InProcessEventBus>? _logger;
    private readonly IServiceProvider            _services;

    public InProcessEventBus(
        IServiceProvider             services,
        IOptions<JsonSerializerOptions> json,
        ILogger<InProcessEventBus>?  logger = null
    ) {
        _services = services;
        _json     = json.Value;
        _logger   = logger;
    }

    #region IEventBus Members

    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : IEvent {
        using var scope    = _services.CreateScope();
        var       store    = scope.ServiceProvider.GetRequiredService<IEventSubscriptionStore>();
        var       resolver = scope.ServiceProvider.GetRequiredService<HandlerResolver>();
        var       options  = scope.ServiceProvider.GetRequiredService<IOptions<SchemataEventOptions>>().Value;
        var       registry = scope.ServiceProvider.GetRequiredService<IEventTypeRegistry>();

        // Enforce the IEventTypeRegistry contract on the in-process path so the same
        // RegisterEvent call covers both in-process and out-of-process buses.
        var name = registry.RequireName(typeof(TEvent));

        var ctx = new EventContext(@event, name) {
            Payload       = JsonSerializer.Serialize(@event, _json),
            CorrelationId = Guid.NewGuid().ToString("n"),
        };
        var adviceCtx = new AdviceContext(scope.ServiceProvider);

        switch (await Advisor.For<IEventPublishAdvisor>().RunAsync(adviceCtx, ctx, ct)) {
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

        try {
            var subscriptions = await store.FindAsync(name, ct: ct);

            var context = scope.ServiceProvider.GetRequiredService<IEventDispatchContext>();
            context.SetSubscriptions(subscriptions);

            var routing = options.RoutingTable.GetValueOrDefault(typeof(TEvent), EventRouting.Broadcast);
            await resolver.InvokeEventHandlersAsync(@event, routing, ct);

            ctx.Result = true;
        } catch (Exception ex) {
            ctx.Exception = ex;
            throw;
        } finally {
            var consumeAdviceCtx = new AdviceContext(scope.ServiceProvider);
            _ = await Advisor.For<IEventConsumeAdvisor>()
                             .RunAsync(consumeAdviceCtx, ctx, ct);
            await NotifyConsumedAsync(observers, ctx, ct);
        }
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
            CorrelationId = Guid.NewGuid().ToString("n"),
        };
        var adviceCtx = new AdviceContext(scope.ServiceProvider);

        switch (await Advisor.For<IEventPublishAdvisor>().RunAsync(adviceCtx, eventCtx, ct)) {
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
            // Same contract as PublishAsync: consume advisors are observational; the
            // handler has either returned or thrown, so all three results converge.
            var consumeAdviceCtx = new AdviceContext(scope.ServiceProvider);
            switch (await Advisor.For<IEventConsumeAdvisor>().RunAsync(consumeAdviceCtx, eventCtx, ct)) {
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

    private static async Task NotifyPublishedAsync(
        IReadOnlyList<IEventLifecycleObserver> observers,
        EventContext                           context,
        CancellationToken                      ct
    ) {
        foreach (var observer in observers) {
            await observer.OnPublishedAsync(context, ct);
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
