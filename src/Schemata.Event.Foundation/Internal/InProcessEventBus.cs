using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Advice;
using Schemata.Event.Skeleton;
using Schemata.Event.Skeleton.Advisors;

namespace Schemata.Event.Foundation.Internal;

public sealed class InProcessEventBus : IEventBus
{
    private readonly JsonSerializerOptions _json;
    private readonly IServiceProvider      _services;

    public InProcessEventBus(IServiceProvider services, IOptions<JsonSerializerOptions> json) {
        _services = services;
        _json     = json.Value;
    }

    #region IEventBus Members

    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : IEvent {
        using var scope    = _services.CreateScope();
        var       store    = scope.ServiceProvider.GetRequiredService<IEventSubscriptionStore>();
        var       resolver = scope.ServiceProvider.GetRequiredService<HandlerResolver>();
        var eventCtx = new EventContext(@event) {
            Payload       = JsonSerializer.Serialize(@event, typeof(TEvent), _json),
            CorrelationId = Guid.NewGuid().ToString("n"),
        };
        var adviceCtx = new AdviceContext(scope.ServiceProvider);

        switch (await Advisor.For<IEventPublishAdvisor>().RunAsync(adviceCtx, eventCtx, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when adviceCtx.TryGet<object>(out var r):
                eventCtx.Result = r;
                return;
            case AdviseResult.Block:
            default:
                throw new InvalidOperationException("Event publish blocked by advisor.");
        }

        try {
            var subscriptions = await store.FindAsync(typeof(TEvent).FullName!, ct: ct);

            var context = scope.ServiceProvider.GetRequiredService<IEventDispatchContext>() as EventDispatchContext;
            context?.SetSubscriptions(subscriptions);

            await resolver.InvokeEventHandlersAsync(@event, ct);

            eventCtx.Result = true;
        } catch (Exception ex) {
            eventCtx.Exception = ex;
            throw;
        } finally {
            var consumeAdviceCtx = new AdviceContext(scope.ServiceProvider);
            await Advisor.For<IEventConsumeAdvisor>().RunAsync(consumeAdviceCtx, eventCtx, ct);
        }
    }

    public async Task<TResponse> SendAsync<TRequest, TResponse>(TRequest request, CancellationToken ct = default)
        where TRequest : IRequest<TResponse> {
        using var scope    = _services.CreateScope();
        var       resolver = scope.ServiceProvider.GetRequiredService<HandlerResolver>();
        var eventCtx = new EventContext(request) {
            Payload       = JsonSerializer.Serialize(request, typeof(TRequest), _json),
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

        try {
            var result = await resolver.InvokeRequestHandlerAsync<TRequest, TResponse>(request, ct);
            eventCtx.Result = result;
            return result;
        } catch (Exception ex) {
            eventCtx.Exception = ex;
            throw;
        } finally {
            var consumeAdviceCtx = new AdviceContext(scope.ServiceProvider);
            await Advisor.For<IEventConsumeAdvisor>().RunAsync(consumeAdviceCtx, eventCtx, ct);
        }
    }

    #endregion
}
