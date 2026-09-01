# Messaging

This guide shows how an application defines a request, registers its one handler, and uses the dispatcher wrap pipeline for request-wide behavior.

## Define a command

```csharp
using System.Threading;
using System.Threading.Tasks;
using Schemata.Messaging.Skeleton;

public sealed record PriceQuery(string Product) : IQuery<decimal>;

public sealed class PriceQueryHandler : IRequestHandler<PriceQuery, decimal>
{
    public Task<decimal> HandleAsync(PriceQuery request, CancellationToken ct = default)
        => Task.FromResult(9.99m);
}
```

Register the handler and dispatch the request:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Schemata.Messaging.Skeleton;

services.AddScoped<IRequestHandler<PriceQuery, decimal>, PriceQueryHandler>();
var price = await dispatcher.SendAsync<PriceQuery, decimal>(new PriceQuery("widget"), ct);
```

`InProcessRequestDispatcher` requires exactly one handler for each dispatched request closure.

## Add a wrap advisor

`IRequestPipelineAdvisor<TRequest,TResponse>` surrounds the handler. Code before `await next(ct)` runs before the handler; code after it runs as the response unwinds.

```csharp
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Schemata.Abstractions.Advisors;
using Schemata.Messaging.Skeleton.Advisors;

public sealed class LoggingPriceQueryAdvisor(ILogger<LoggingPriceQueryAdvisor> logger)
    : IRequestPipelineAdvisor<PriceQuery, decimal>
{
    public int Order => 0;

    public async Task<decimal> AdviseAsync(
        AdviceContext ctx,
        PriceQuery request,
        RequestHandlerContinuation<decimal> next,
        CancellationToken ct = default) {
        logger.LogInformation("Dispatching {Product}", request.Product);
        return await next(ct);
    }
}

services.TryAddEnumerable(ServiceDescriptor.Scoped<
    IRequestPipelineAdvisor<PriceQuery, decimal>,
    LoggingPriceQueryAdvisor>());
```

The dispatcher sorts registered wraps by `Order`. A wrap can return a response without calling its continuation or throw an exception to terminate the request. Plain `IRequest<TResponse>` requests invoke their handler without a wrap chain.

## Ambient context

The dispatcher establishes `AdviceContext` for one in-process dispatch. Wrap advisors and the handler share it. Use it for pipeline coordination markers; keep request and response business data in typed envelopes or local variables.

Nested dispatch restores the outer ambient context after the inner request returns. A handler-stage advisor reads the existing context with `AdviceContext.Require()`.

## RabbitMQ dispatch

`AddRabbitMqRequestDispatcher` replaces the dispatcher interfaces for outbound requests. The RabbitMQ consumer host resolves a handler for a delivered request. Put policy that must run on every delivery in the receiving handler when the same request can also arrive through the broker.

## Next steps

- [Messaging overview](../documents/messaging/overview.md)
- [Resource overview](../documents/resource/overview.md)
- [Flow](flow.md)
