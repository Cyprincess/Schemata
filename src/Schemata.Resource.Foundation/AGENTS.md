# Schemata.Resource.Foundation

Google AIP-compliant resource service. CRUD + List/Search over typed handlers, surfaced through HTTP (`Schemata.Resource.Http`, `MapHttp`) and gRPC (`Schemata.Resource.Grpc`, `MapGrpc`). Reference: [google.aip.dev/general](https://google.aip.dev/general).

## Activation

```csharp
schema.UseResource()
      .MapHttp(...)    // adds SchemataHttpResourceFeature  (490_100_000)
      .MapGrpc(...);   // adds SchemataGrpcResourceFeature  (490_200_000)
```

`SchemataResourceFeature` sits at `Priority 490_000_000`.

## Handler Contract

Every resource method registers a class implementing
`IResourceMethodHandler<TEntity, TRequest, TResponse>`. Registration fails when the type does not implement this interface - see [Features/SchemataResourceFeature.cs](file:///D:/source/repos/Cyprin/Schemata/src/Schemata.Resource.Foundation/Features/SchemataResourceFeature.cs).

Built-in soft-delete methods (`Undelete`, the soft-aware `Delete`) are added only when the entity implements `Schemata.Abstractions.Entities.ISoftDelete`. Do not register them for non-soft-delete entities.

## Standard Surface

Per AIP: `Create`, `Get`, `List`, `Search`, `Update`, `Delete`, `Undelete`, `BatchGet`, `BatchUpdate`, `BatchDelete`. Resource names use the AIP path scheme; bindings are produced by the resource builder, not hand-routed.

## Errors & Field Reasons

Use `SchemataConstants.ErrorCodes` (`NOT_FOUND`, `INVALID_ARGUMENT`, `FAILED_PRECONDITION`, ...) and `FieldReasons` (`invalid_filter`, `invalid_update_mask`, `invalid_read_mask`, `invalid_order_by`, `invalid_page_token`, `invalid_page_size`, `cross_parent_unsupported`, `invalid_parent`, ...) for AIP-style structured errors. Never invent new strings here.

## Rules

- Filters, order-by, read masks, and update masks are CEL/AIP expressions and route through `Schemata.Expressions.*`. Do not parse them manually.
- Page sizes must be non-negative; the foundation rejects with `INVALID_ARGUMENT` + `invalid_page_size`. Do not relax this.
- `MapHttp` and `MapGrpc` are mutually compatible; a resource may expose both. The Foundation feature is auto-pulled by both bridges via `[DependsOn]`.
- Resource handlers are scoped per-request; do not store mutable state on handler instances.
