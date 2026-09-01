# Messaging

`Schemata.Messaging.Skeleton` provides one request/reply dispatcher for commands and queries. A request has one handler and one response; events remain a separate broadcast abstraction.

## Packages

| Package | Role |
| --- | --- |
| `Schemata.Messaging.Skeleton` | Request contracts, handlers, dispatchers, `IRequestPipelineAdvisor<,>`, method envelopes, `InProcessRequestDispatcher`, and message context contracts |
| `Schemata.Messaging.RabbitMq` | Request/reply dispatch over RabbitMQ |

## Dispatcher pipeline

A module registers `InProcessRequestDispatcher` and forwards `IRequestDispatcher`, `ICommandDispatcher`, and `IQueryDispatcher` to that scoped implementation. `SendAsync` establishes one ambient `AdviceContext`, resolves exactly one `IRequestHandler<TRequest,TResponse>` at the continuation tail, and composes registered `IRequestPipelineAdvisor<TRequest,TResponse>` instances around that tail for commands and queries.

```csharp
public sealed class AuditCreateOrder
    : IRequestPipelineAdvisor<CreateOrder, OrderResponse>
{
    public int Order => 0;

    public async Task<OrderResponse> AdviseAsync(
        AdviceContext ctx,
        CreateOrder request,
        RequestHandlerContinuation<OrderResponse> next,
        CancellationToken ct = default) {
        var response = await next(ct);
        return response;
    }
}
```

The dispatcher sorts wraps in ascending `Order`. The segment before `await next(ct)` runs before the handler. The segment after it runs while the chain unwinds in reverse order and can reshape the response. An advisor can return a response without calling `next`, or throw to terminate the dispatch. Plain `IRequest<TResponse>` requests bypass the wrap chain and invoke their handler directly.

## Ambient context

The dispatcher establishes `AdviceContext` for one dispatch and restores the previous ambient value when it returns. Wrap advisors and handlers share that instance. Handler-local advisor stages continue the existing ambient context with `AdviceContext.Require()`.

`AdviceContext` carries pipeline coordination and configuration markers. Request and response payloads, cache keys, hashes, entities, and other business data stay in envelopes or advisor-local state. Insight's direct `PlanExecutor` fallback and Authorization's sign-in entry create an ambient context only when no dispatch context exists. Flow transition and source stages continue the ambient context when present.

## Method envelopes

`ResourceMethodRequest<TEntity,TRequest,TResponse>` carries a custom method's lower-camel-case verb, optional instance name, payload, and caller principal. Resource custom methods and Flow, Report, and Scheduling method operations enter the dispatcher through this envelope. Security wrap advisors can therefore resolve the verb and resource type before a handler runs.

A domain can forward an envelope through `ResourceMethodForwardHandler<TEntity,TRequest,TResponse>`, which copies the envelope principal to an inner request that implements `IRequestPrincipal` and dispatches that request. Resource methods needing Resource handler stages use `ResourceMethodDispatchHandler` instead.

## Handler registration

`InProcessRequestDispatcher` requires exactly one handler for each dispatched request closure. Zero handlers and multiple handlers throw `InvalidOperationException`. Register custom handlers behind `IRequestHandler<TRequest,TResponse>`.

## RabbitMQ

`RabbitMqRequestDispatcher` implements the same dispatcher interfaces for client-side delivery. Its consumer host invokes the resolved handler without the in-process wrap chain. Place delivery-wide policy in the receiving handler when a request can arrive through both paths.

## See also

- [Advice pipeline](../core/advice-pipeline.md)
- [Resource overview](../resource/overview.md)
- [Flow overview](../flow/overview.md)
