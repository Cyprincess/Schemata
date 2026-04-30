# Security

Schemata provides a pluggable security model with two provider interfaces: one for entity-level access control and one for query-level row filtering. Default allow-all implementations are registered automatically, and domain-specific providers ship with the resource and workflow subsystems.

## Packages

| Package                        | Role                                                   |
| ------------------------------ | ------------------------------------------------------ |
| `Schemata.Security.Skeleton`   | Core interfaces, defaults, and registration extensions |
| `Schemata.Security.Foundation` | Feature registration via `UseSecurity()`               |
| `Schemata.Resource.Foundation` | `ResourceAccessProvider` for resource CRUD             |
| `Schemata.Workflow.Skeleton`   | `WorkflowAccessProvider` for workflow operations       |

## IAccessProvider\<T, TContext\>

Determines whether a principal has access to a specific entity within a given context:

```csharp
public interface IAccessProvider<T, TContext>
{
    Task<bool> HasAccessAsync(
        T?                entity,
        TContext?         context,
        ClaimsPrincipal?  principal,
        CancellationToken ct = default
    );
}
```

- `entity` -- the entity being accessed, or `null` for collection-level checks
- `context` -- carries additional authorization state (e.g. the operation type, the request DTO)
- `principal` -- the authenticated user's claims

Returns `true` to grant access, `false` to deny.

## IEntitlementProvider\<T, TContext\>

Generates LINQ filter expressions that restrict data visibility at the query level:

```csharp
public interface IEntitlementProvider<T, TContext>
{
    Task<Expression<Func<T, bool>>?> GenerateEntitlementExpressionAsync(
        TContext?         context,
        ClaimsPrincipal?  principal,
        CancellationToken ct = default
    );
}
```

Returns a predicate expression that is composed into repository queries, ensuring unauthorized entities are excluded at the data layer rather than being filtered after retrieval. Returning `null` applies no additional filtering.

## Built-in implementations

### DefaultAccessProvider\<T, TContext\>

The fallback access provider. Always returns `true`, granting access to all entities unconditionally. Registered as an open-generic fallback so that custom providers registered before `UseSecurity()` take precedence.

### DefaultEntitlementProvider\<T, TContext\>

The fallback entitlement provider. Returns `_ => true`, granting visibility to all rows. Replace with a custom implementation to enforce row-level security.

### ResourceAccessProvider\<T, TRequest\>

Located in `Schemata.Resource.Foundation.Security`. Checks the principal's role claims for resource operations using the format:

```
resource-{operation}-{entity}
```

Where:

- `{operation}` is the CRUD operation, kebab-cased via Humanizer (e.g. `create`, `update`, `delete`, `list`)
- `{entity}` is the entity type name, kebab-cased (e.g. `product`, `order-item`)

Supports wildcards:

- `resource-*-product` -- grants all operations on `Product`
- `resource-create-*` -- grants create on all entity types

Requires a non-null `ClaimsPrincipal`; returns `false` when the principal is `null`.

The context type is `ResourceRequestContext<TRequest>`, which carries the `Operation` value.

### WorkflowAccessProvider\<T, TRequest\>

Located in `Schemata.Workflow.Skeleton.Security`. Uses the same pattern as the resource provider but with a different prefix:

```
workflow-{operation}-{entity}
```

Where `{operation}` corresponds to workflow operations (e.g. `get`, `submit`, `raise`), kebab-cased.

Supports the same wildcard patterns:

- `workflow-*-approval` -- grants all operations on `Approval` workflows
- `workflow-get-*` -- grants read access to all workflow types

The context type is `WorkflowRequestContext<TRequest>`, which carries the `Operation` string and the associated `SchemataWorkflow`.

## Registration

### UseSecurity()

Registers the `SchemataSecurityFeature` on the builder:

```csharp
builder.UseSecurity();
```

This registers `DefaultAccessProvider<,>` and `DefaultEntitlementProvider<,>` as open-generic scoped services using `TryAddScoped`, meaning any providers registered earlier are not overwritten.

### AddAccessProvider

Register a custom access provider using the generic extension method:

```csharp
services.AddAccessProvider<Product, ResourceRequestContext<CreateProductRequest>, ProductAccessProvider>();
```

Or using runtime types:

```csharp
services.AddAccessProvider(entityType, contextType, providerType);
```

Both use `TryAddScoped` internally, so the first registration for a given `IAccessProvider<T, TContext>` wins.

### AddEntitlementProvider

Register a custom entitlement provider:

```csharp
services.AddEntitlementProvider(typeof(ProductEntitlementProvider));
```

Uses `TryAddScoped` with the open-generic `IEntitlementProvider<,>` service type.

## Try-Add pattern

All registrations use the `TryAdd` family of methods. This means:

1. Register your custom providers first (in `ConfigureServices` or before calling `UseSecurity()`)
2. The framework's default providers only fill gaps where no custom provider exists
3. For a given `IAccessProvider<T, TContext>`, only the first registered implementation is used
4. The open-generic defaults (`DefaultAccessProvider<,>` and `DefaultEntitlementProvider<,>`) serve as catch-all fallbacks for any `T`/`TContext` combination that does not have an explicit registration
