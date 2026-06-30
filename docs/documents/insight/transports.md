# Transports

Insight Foundation owns planning and execution. Transport packages expose that service over HTTP and
gRPC, map edge messages into the core wire types, and translate `InsightValidationException` into the
shared Schemata error model.

## Feature priorities

`SchemataInsightFeature.DefaultPriority` is `Orders.Extension + 95_000_000` = 495,000,000.

| Feature                      | Priority    | Depends on                                               |
| ---------------------------- | ----------- | -------------------------------------------------------- |
| `SchemataInsightFeature`     | 495,000,000 | none                                                     |
| `SchemataInsightHttpFeature` | 495,100,000 | `SchemataInsightFeature`, `SchemataTransportHttpFeature` |
| `SchemataInsightGrpcFeature` | 495,200,000 | `SchemataInsightFeature`, `SchemataTransportGrpcFeature` |

HTTP and gRPC transports are activated from the `SchemataInsightBuilder` returned by `UseInsight()`:

```csharp
schema.UseInsight(i => {
    i.AddRepositorySource("students", "students")
     .AddSourceDriver<RepositoryDriver>(RepositoryDriver.DriverName);
}).MapHttp();
```

```csharp
schema.UseInsight(i => {
    i.AddRepositorySource("buyers", "buyers")
     .AddSourceDriver<RepositoryDriver>(RepositoryDriver.DriverName);
}).MapGrpc();
```

## HTTP

`Schemata.Insight.Http.Features.SchemataInsightHttpFeature` registers this assembly's
`InsightController` as an MVC application part. The shared HTTP transport feature supplies exception
handling and JSON wire-name behavior.

`InsightController.QueryAsync` exposes one custom method:

```text
POST /v1/insight:query
```

The action accepts `QueryInsightRequest` from the JSON body, passes `HttpContext.User` and
`HttpContext.RequestAborted` to `IInsightService.QueryAsync`, and returns `QueryInsightResponse` with
the host `JsonSerializerOptions`.

### HTTP request shape

```json
{
  "sources": [{ "alias": "s", "name": "students" }],
  "transformations": [
    { "filter": { "predicate": { "source": "age > 20" } } },
    { "order_by": { "order_by": "age desc" } }
  ],
  "selections": [{ "field": "s.full_name" }],
  "page_size": 1
}
```

The default Schemata JSON settings use snake_case names, so `PageSize` is `page_size`,
`NextPageToken` is `next_page_token`, and `TotalSize` is `total_size`.

### HTTP error translation

`InsightController` catches `InsightValidationException` and throws a `SchemataException`:

| Insight reason        | HTTP status |
| --------------------- | ----------- |
| `UNKNOWN_SOURCE_NAME` | 404         |
| `UNIMPLEMENTED`       | 501         |
| any other reason      | 400         |

The exception message becomes the AIP-193 error message, and the reason remains the Schemata exception
code.

## gRPC

`Schemata.Insight.Grpc` exposes a code-first gRPC service:

```csharp
[Service]
public interface IInsightGrpcService
{
    [Operation]
    ValueTask<QueryInsightGrpcResponse> QueryAsync(
        QueryInsightGrpcRequest request,
        CallContext             context = default);
}
```

`SchemataInsightGrpcFeature` registers `InsightGrpcService` as scoped, registers
`InsightServiceMethodProvider` as an `IServiceMethodProvider<InsightGrpcService>`, and maps the service
with `endpoints.MapGrpcService<InsightGrpcService>()`.

`InsightGrpcMethods.Query` defines the unary method:

| Member       | Value                                |
| ------------ | ------------------------------------ |
| service name | `schemata.insight.v1.InsightService` |
| method name  | `Query`                              |
| request      | `QueryInsightGrpcRequest`            |
| response     | `QueryInsightGrpcResponse`           |

## gRPC wire messages

The gRPC request mirrors the core request with protobuf-net message classes:

| Core type             | gRPC type                  |
| --------------------- | -------------------------- |
| `InsightExpression`   | `InsightExpressionMessage` |
| `SourceBinding`       | `SourceBindingMessage`     |
| `JoinSpec`            | `JoinSpecMessage`          |
| `TransformationSpec`  | `TransformationMessage`    |
| `ComputedFieldSpec`   | `ComputedFieldMessage`     |
| `AggregationSpec`     | `AggregationMessage`       |
| `SelectionSpec`       | `SelectionMessage`         |
| `QueryInsightRequest` | `QueryInsightGrpcRequest`  |

The gRPC response uses `InsightStruct` and `InsightValue` for dynamic rows. `InsightValue` has typed
slots for string, number, integer, bool, struct, list, and null values. `FieldDescriptorMessage` mirrors
`FieldDescriptor` for schema output.

## InsightStructMapper

`InsightStructMapper` maps the edge messages to the core wire types:

- `ToRequest(QueryInsightGrpcRequest)` copies sources, joins, transformations, selections, paging, and
  request language into `QueryInsightRequest`.
- `ToResponse(QueryInsightResponse)` maps dynamic dictionary rows into `InsightStruct` values and maps
  the schema tree into `FieldDescriptorMessage` values.
- `ToStruct(IReadOnlyDictionary<string, object?>)` converts nested dictionaries and lists into
  `InsightValue` trees.

`TransformationMessage` uses nullable members for filter, compute, order, top, and skip. Group-by uses
`IsGroupBy` plus `GroupByKeys` and `GroupByAggregations` so an empty aggregation list can still mean a
requested group-by.

## InsightServiceMethodProvider

`InsightServiceMethodProvider` binds the method at gRPC discovery time:

```csharp
context.AddUnaryMethod(
    InsightGrpcMethods.Query,
    [],
    async (service, request, call) => await service.QueryAsync(request, new(service, call)));
```

The shared `InsightGrpcMethods.Query` object keeps server registration and direct test clients on the
same service name, method name, and protobuf-net marshallers.

## gRPC error translation

`InsightGrpcService` catches `InsightValidationException` and throws `SchemataException`. The shared
gRPC interceptor maps the Schemata exception to `RpcException` status:

| Insight reason        | Schemata code                      | gRPC status       |
| --------------------- | ---------------------------------- | ----------------- |
| `UNKNOWN_SOURCE_NAME` | 404 / `ErrorCodes.NotFound`        | `NotFound`        |
| any other reason      | 400 / `ErrorCodes.InvalidArgument` | `InvalidArgument` |

The current gRPC translator does not special-case `UNIMPLEMENTED`; it reports it as InvalidArgument.

## See also

- [Overview](overview.md) — package layout and startup
- [Planning](planning.md) — validation reasons before transport translation
- [Drivers](drivers.md) — source execution and errors
- [gRPC Transport Guide](../../guides/grpc-transport.md) — enabling gRPC in the Student app
