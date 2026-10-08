# Transports

Insight Foundation owns planning, execution and canonical exceptions. Transports dispatch the same
query handler and pass `InsightValidationException`, a `SchemataException`, to the shared error
boundary. HTTP JSON and gRPC status trailers retain its reason, domain and metadata.

## Feature priorities

`SchemataInsightFeature.DefaultPriority` is `Orders.Extension + 110_000_000` = 510,000,000.

| Feature                      | Priority    | Depends on                                               |
| ---------------------------- | ----------- | -------------------------------------------------------- |
| `SchemataInsightFeature`     | 510,000,000 | none                                                     |
| `SchemataInsightHttpFeature` | 510,100,000 | `SchemataInsightFeature`, `SchemataTransportHttpFeature` |
| `SchemataInsightGrpcFeature` | 510,200,000 | `SchemataInsightFeature`, `SchemataTransportGrpcFeature` |

HTTP and gRPC transports are activated from the `SchemataInsightBuilder` returned by `UseInsight()`:

```csharp
using Schemata.Insight.Foundation.Drivers;

schema.UseInsight(i => {
    i.AddRepositorySource<Student, StudentRow>("students",
            s => new StudentRow { FullName = s.FullName, Age = s.Age })
     .AddSourceDriver<RepositoryDriver>(RepositoryDriver.DriverName);
}).MapHttp();

public sealed class StudentRow
{
    public string? FullName { get; set; }
    public int     Age      { get; set; }
}
```

```csharp
using Schemata.Insight.Foundation.Drivers;

schema.UseInsight(i => {
    i.AddRepositorySource<Buyer, BuyerRow>("buyers",
            b => new BuyerRow { Id = b.Id, FullName = b.FullName })
     .AddSourceDriver<RepositoryDriver>(RepositoryDriver.DriverName);
}).MapGrpc();

public sealed class BuyerRow
{
    public int     Id       { get; set; }
    public string? FullName { get; set; }
}
```

## HTTP

`Schemata.Insight.Http.Features.SchemataInsightHttpFeature` registers this assembly's
`InsightController` as an MVC application part. The shared HTTP transport feature supplies exception
handling and JSON wire-name behavior.

`InsightController.QueryAsync` exposes one custom method:

```text
POST /v1/insight:query
```

The action accepts `QueryInsightRequest`, stamps `HttpContext.User`, dispatches through
`IRequestDispatcher` with `HttpContext.RequestAborted`, and serializes `QueryInsightResponse` using
the host JSON options. Insight normalizes enum values to their declared names within its own rows.

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

### Canonical errors

`Planning/InsightValidationException.cs` in `Schemata.Insight.Foundation` owns the classification:

| Insight reason | HTTP code | Canonical status | gRPC status |
| --- | --- | --- | --- |
| `UNKNOWN_SOURCE_NAME` | 404 | `NOT_FOUND` | `NotFound` |
| `UNIMPLEMENTED` | 501 | `UNIMPLEMENTED` | `Unimplemented` |
| other request rejections | 400 | `INVALID_ARGUMENT` | `InvalidArgument` |

`ErrorInfo.reason` retains the Insight reason and `ErrorInfo.domain` is `schemata.insight`.
Named metadata renders the invariant developer message and locale-specific detail from the same
resource template. Transport adapters propagate the domain exception directly.

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

The gRPC response uses `DynamicStruct` and `DynamicValue` from `Schemata.Transport.Grpc.Wire`
for dynamic rows. Scalar slots cover string, number, integer, bool and null. `ListValue` is a
`DynamicList` submessage whose `Values` preserve empty-list presence. `StructValue` preserves
empty maps. `DynamicFieldDescriptor<FieldType>` carries the domain field labels, including
`Element` for nested sequence ranks. Report snapshot pages use the same wire shapes.

## InsightStructMapper

`InsightStructMapper` maps the edge messages to the core wire types:

- `ToRequest(QueryInsightGrpcRequest)` copies sources, joins, transformations, selections, paging, and
  request language into `QueryInsightRequest`.
- `ToResponse(QueryInsightResponse)` maps rows through `DynamicValueMapper` and maps the schema
  into `DynamicFieldDescriptor<FieldType>` values.

### Dynamic value table

`ScalarValue` in `Schemata.Common` owns scalar classification and exact formatting. Insight's
`InsightValueModel` maps those classifications to the domain `FieldType` and owns rejected-value
errors and HTTP enum projection. `DynamicValueMapper` uses the same scalar table for Insight and
Report. Consumers use `FieldDescriptor.Type` to restore values; string contents do not identify
their type.

| Public value | Schema type | gRPC slot / encoding |
| --- | --- | --- |
| signed integers, byte, ushort, uint | `Int64` | `IntValue` |
| ulong | `UInt64` | `IntValue` through long.MaxValue; unsigned decimal `StringValue` above it |
| float, double | `Double` | `NumberValue` |
| decimal | `Decimal` | invariant exact decimal `StringValue` |
| bool / null | `Bool` / declared nullable type | `BoolValue` / `NullValue` |
| string / char | `String` / `Char` | `StringValue` |
| byte[] | `Bytes` | base64 `StringValue` |
| Guid | `Guid` | `D` format `StringValue` |
| DateTime / DateTimeOffset | `Timestamp` / `DateTimeOffset` | round-trip `O` format `StringValue` |
| TimeSpan | `Duration` | invariant `c` format `StringValue` |
| enum | `Enum` | declared named value `StringValue`; undefined values are rejected |
| map | `Map` or object fields | `StructValue`; map value schema is the `*` child |
| list | `IsList=true` with `Element` descriptor | `ListValue.Values`; nested lists retain each element rank |

HTTP may encode decimal and ulong as JSON numbers, and encodes bytes as base64. Both transports
restore the same values using the schema. `RowMaterializer.NormalizeRow(row, schema)` adapts custom
driver CLR children only through explicit object child descriptors and cached public getter bindings.
Typed dictionaries inside those children use their declared map value types and the map schema's
`*` descriptor. Getter bindings use weak schema-array keys so released result schemas can be collected.
Ignored or undeclared CLR members remain outside the query model. Repository projections use their
declared public type. Unknown objects without a declared object schema and cyclic rows are rejected
before serialization.

Unknown expression and Compute outputs carry `FieldType.Dynamic` from planning onward. Their scalar
alternatives remain distinct through grouping and serialization. Known source fields keep their
existing schema-directed representation. Aliases copy metadata from the selected input path;
nested Group and Compute stages transform child descriptors before result validation.
Source qualifiers come from the plan's declared source aliases and are removed before input-field
metadata lookup, including when an alias matches another public property. Nested grouped output
qualifiers resolve through `IQualifiedExpressionContext` in Expressions.Skeleton, independently of
the dictionary's bare output slots. An aggregate named `o` remains a scalar under bare `o`, while
qualified `o.o` resolves that grouped field. CEL root-member compilation and local field/order lookup
share this precedence; lexical expression locals retain ordinary member resolution. Original
unselected columns remain outside the grouped environment, and rows contain no synthetic alias key.

Persisted JSON containers with mixed known and Dynamic children are traversed by schema. Uniform
JsonElement leaves retain their original numeric/string/container tokens; only Dynamic descendants
are decoded and re-encoded. HTTP response deserialize/serialize cycles preserve that boundary.

HTTP Dynamic values use a typed payload: `{"kind":"int64","value":"1"}` differs from
`{"kind":"string","value":"1"}`. `ScalarPayloadConverter` in Common writes canonical scalar strings
using `ScalarValue.Kind` and `Format`, and restores them using `Parse`. A null payload is JSON null;
maps and lists use `kind=object` with recursively typed children, preserving empty containers.
Named enums retain a symbolic declared name plus the `Enum` label, matching the uniform field's public
name semantics. Dynamic reconstruction represents that pair with `ScalarPayload`; it never loads a
CLR enum type from wire data. Unsupported CLR objects remain rejected.

gRPC Dynamic leaves retain the existing scalar slots and add optional `DynamicValue.TypeLabel`
(field 8), a nullable `ScalarKind` enum. Integer, number, bool and string-backed kinds require their
matching slot. Null and container values carry no scalar label. The reflected proto2 descriptor
publishes enum presence, and `DynamicValueMapper.FromDynamic` rejects conflicting labels or slots.



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

`InsightGrpcServiceDescriptorContributor` generates reflection descriptors through `GrpcSchema`
from `InsightGrpcMethods.Model` and the bound method definitions. Shared transport registration
adds the exception interceptor once across repeated domain installations. Existing proto-first
services retain their generated `BindServiceMethod` descriptor discovery.

## gRPC errors

The shared `ExceptionMappingInterceptor` consumes the domain exception and writes
`grpc-status-details-bin` containing `google.rpc.Status`. Its `ErrorInfo` carries the same reason,
domain and metadata as the HTTP envelope. `RpcStatusBuilder` maps the canonical `UNIMPLEMENTED`
status to gRPC `Unimplemented`.

## See also

- [Overview](overview.md) — package layout and startup
- [Planning](planning.md) — validation reasons before transport translation
- [Drivers](drivers.md) — source execution and errors
- [gRPC Transport Guide](../../guides/grpc-transport.md) — enabling gRPC in the Student app
