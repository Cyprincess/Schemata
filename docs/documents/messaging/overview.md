# Messaging

`Schemata.Messaging.Skeleton` provides one request/reply dispatcher for commands and queries. A request has one handler and one response; events remain a separate broadcast abstraction.

## Packages

| Package | Role |
| --- | --- |
| `Schemata.Messaging.Skeleton` | Request contracts, handlers, dispatchers, `IRequestPipelineAdvisor<,>`, method envelopes, `InProcessRequestDispatcher`, and message context contracts |
| `Schemata.Messaging.RabbitMq` | Request/reply dispatch over RabbitMQ |

## Dispatcher pipeline

A module registers the in-process dispatcher through `AddInProcessRequestDispatcher()` (`Messaging.Skeleton`), the single entry point that registers the concrete `InProcessRequestDispatcher` and binds `IRequestDispatcher`, `ICommandDispatcher`, and `IQueryDispatcher` to it — one scoped instance owns command, query, and raw dispatch. `SendAsync` establishes one ambient `AdviceContext`, resolves exactly one `IRequestHandler<TRequest,TResponse>` at the continuation tail, and composes registered `IRequestPipelineAdvisor<TRequest,TResponse>` instances around that tail for commands and queries.

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

The dispatcher sorts wraps in ascending `Order`. Before segments run before the handler; after segments
unwind in reverse. Plain `IRequest<TResponse>` requests run only an explicitly installed keyed validation
stage; their ordinary wraps are neither constructed nor executed. Commands and queries merge that single
stage into their ordered chain. See [Validation](../validation.md).

## Ambient context

The dispatcher establishes `AdviceContext` for one dispatch and restores the previous ambient value when it returns. Wrap advisors and handlers share that instance. Handler-local advisor stages continue the existing ambient context with `AdviceContext.Require()`.

`AdviceContext` carries pipeline coordination and configuration markers. Request and response payloads, cache keys, hashes, entities, and other business data stay in envelopes or advisor-local state. Insight's direct `PlanExecutor` fallback and Authorization's sign-in entry create an ambient context only when no dispatch context exists. Flow transition and source stages continue the ambient context when present.

## Method envelopes

`ResourceMethodRequest<TEntity,TRequest,TResponse>` carries a custom method's lower-camel-case verb, optional instance name, payload, and caller principal. Resource custom methods and Flow, Report, and Scheduling method operations enter the dispatcher through this envelope. Security wrap advisors can therefore resolve the verb and resource type before a handler runs.

A domain can forward an envelope through `ResourceMethodForwardHandler<TEntity,TRequest,TResponse>`, which copies the envelope principal to an inner request that implements `IRequestPrincipal` and dispatches that request. Resource methods needing Resource handler stages use `ResourceMethodDispatchHandler` instead.

## Handler registration

`InProcessRequestDispatcher` requires exactly one handler for each dispatched request closure. Zero handlers and multiple handlers throw `InvalidOperationException`. Register custom handlers behind `IRequestHandler<TRequest,TResponse>`.

## Scoped streams

`AddSchemataStreams()` registers `IStreamDispatcher`. Its `Stream<TRequest,TItem>(request, principal, ct)`
returns a cold `IAsyncEnumerable<TItem>` for an `IStreamRequest<TItem>`. Register one
`IStreamRequestHandler<TRequest,TItem>`; its `HandleAsync` receives the request, `StreamExecutionContext`,
and cancellation token. Payloads remain caller-owned and must not change during enumeration. Use the
context's captured principal for stream policy, rather than reading mutable principal fields on a payload.

Invocation captures message context and clones caller identities. Each enumeration creates its own
message execution scope, principal copy, and `AdviceContext` on its first move. Every move and disposal
enters the captured tenant and advice frames and restores the consumer's previous frames afterward.
Completion, failure, cancellation, and early disposal release the scope once; an unenumerated sequence
creates no scope. Distinct enumerators may run concurrently; calls on one enumerator must be serialized.

`IStreamPipelineAdvisor<TRequest,TItem>` surrounds enumeration through `StreamContinuation<TItem>`.
Admission can reject before the handler is resolved; iterator `finally` blocks run before scope disposal.
Invocation and enumeration cancellation tokens are combined. Primary and cleanup failures are retained
when both occur. Tenant-aware hosts supply the existing `IMessageExecutionScopeFactory`; the default
factory supports host identity only. Unary `SendAsync` returning a lazy object retains unary lifetime
semantics and does not substitute for this stream API.

## RabbitMQ

`RabbitMqRequestDispatcher` implements the same dispatcher interfaces for client-side delivery. `AddRabbitMqRequestDispatcher` binds `IRequestDispatcher`, `ICommandDispatcher`, and `IQueryDispatcher` with `TryAdd`, so whichever dispatcher registration lands first owns the public slots; a host that wants broker delivery registers the RabbitMQ dispatcher before module capability extensions. The consumer host re-dispatches each consumed request through the `InProcessRequestDispatcher` concrete, so consumed requests run the same advisor chain as local dispatches.

RabbitMQ rejects `IStreamDispatcher.Stream` and unary response contracts implementing `IAsyncEnumerable<T>`
with `NotSupportedException` before opening a connection or publishing a request.

See [Streams](streams.md) for the HTTP and gRPC adapters over `IStreamDispatcher`.

## See also

- [Advice pipeline](../core/advice-pipeline.md)
- [Resource overview](../resource/overview.md)
- [Flow overview](../flow/overview.md)
