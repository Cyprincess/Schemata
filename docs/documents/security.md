# Security

`Schemata.Security.Foundation` provides a pluggable security model built on two provider interfaces: `IAccessProvider<T, TRequest>` for entity-level access decisions and `IEntitlementProvider<T, TRequest>` for row-level query filtering. Calling `UseSecurity()` on `SchemataBuilder` registers the feature at priority 400,000,000 and installs allow-all default implementations via `TryAddScoped`, so any custom providers registered earlier take precedence.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Security.Skeleton` | `IAccessProvider.cs`, `IEntitlementProvider.cs`, `IPermissionResolver.cs`, `IPermissionMatcher.cs`, `AccessContext.cs` |
| `Schemata.Security.Foundation` | `Extensions/SchemataBuilderExtensions.cs` — `UseSecurity()` |
| `Schemata.Security.Foundation` | `Features/SchemataSecurityFeature.cs` — priority 400,000,000 |
| `Schemata.Security.Foundation` | `DefaultAccessProvider.cs`, `DefaultEntitlementProvider.cs`, `DefaultPermissionResolver.cs`, `DefaultPermissionMatcher.cs` |

## Mechanism walkthrough

### 1. Enable the feature

```csharp
builder.UseSchemata(schema => {
    schema.UseSecurity();
    // or with options:
    schema.UseSecurity(o => o.SomeOption = value);
});
```

`UseSecurity` stores the optional `Action<SchemataSecurityOptions>` in `Configurators` and calls `builder.AddFeature<SchemataSecurityFeature>()`.

### 2. What the feature registers

`SchemataSecurityFeature.ConfigureServices` calls `TryAddScoped` for four services:

```csharp
services.TryAddScoped<IPermissionResolver, DefaultPermissionResolver>();
services.TryAddScoped<IPermissionMatcher, DefaultPermissionMatcher>();
services.TryAddScoped(typeof(IAccessProvider<,>), typeof(DefaultAccessProvider<,>));
services.TryAddScoped(typeof(IEntitlementProvider<,>), typeof(DefaultEntitlementProvider<,>));
```

Because all four use `TryAdd`, any implementation registered before `UseSecurity()` is called wins.

### 3. Access check flow

The resource authorization advisors (e.g., `AdviceCreateRequestAuthorize`, `AdviceUpdateRequestAuthorize`) run at Order = 100,000,000 and resolve `IAccessProvider<TEntity, TRequest>` from DI to call `HasAccessAsync`. The signature:

```csharp
public interface IAccessProvider<T, TRequest>
{
    Task<bool> HasAccessAsync(
        T?                      entity,
        AccessContext<TRequest> context,
        ClaimsPrincipal?        principal,
        CancellationToken       ct = default
    );
}
```

`entity` is `null` for collection-level checks (List). `context` carries the operation kind and the request DTO. Returning `false` causes the advisor to return `AdviseResult.Block`, which short-circuits to a `Blocked` result before the entity is touched.

### 4. Row-level filtering

`IEntitlementProvider<T, TRequest>` generates a LINQ predicate that is composed into repository queries:

```csharp
public interface IEntitlementProvider<T, TRequest>
{
    Task<Expression<Func<T, bool>>?> GenerateEntitlementExpressionAsync(
        AccessContext<TRequest> context,
        ClaimsPrincipal?        principal,
        CancellationToken       ct = default
    );
}
```

Returning `null` applies no additional filtering. Returning a predicate means unauthorized rows are excluded at the data layer, not after retrieval.

### 5. Permission resolution

`IPermissionResolver` converts an operation name and entity type to a permission string:

```csharp
public interface IPermissionResolver
{
    string Resolve(string operation, Type entity);
}
```

`DefaultPermissionResolver` produces strings in the format `{operation}:{entity}` (kebab-cased). `IPermissionMatcher` then checks whether the principal holds that permission.

## Extension points

| Interface | Purpose |
| --- | --- |
| `IAccessProvider<T, TRequest>` | Entity-level access gate. Register via `services.TryAddScoped` before `UseSecurity()`. |
| `IEntitlementProvider<T, TRequest>` | Row-level query filter. Same registration pattern. |
| `IPermissionResolver` | Converts operation + entity type to a permission string. |
| `IPermissionMatcher` | Checks whether a principal holds a resolved permission. |

## Design motivation

Separating `IAccessProvider` (entity-level) from `IEntitlementProvider` (query-level) lets you enforce access at two independent points. The access provider gates individual operations; the entitlement provider ensures that even bulk queries only return rows the principal is allowed to see. Both are open-generic so a single implementation can cover all entity types, while specific implementations for particular entities take precedence via `TryAdd` ordering.

`IPermissionResolver` and `IPermissionMatcher` are split so the string format (resolver) and the claim-matching logic (matcher) can be replaced independently.

## Caveats

- `SchemataSecurityFeature` has `Priority = Orders.Extension = 400_000_000`. See [Built-in Features](core/built-in-features.md) for the full priority table.
- All four registrations use `TryAddScoped`. Register custom providers before calling `UseSecurity()` to ensure they take precedence over the defaults.
- `DefaultAccessProvider<,>` always returns `true`. Without a custom provider, all principals have access to all entities.
- `DefaultEntitlementProvider<,>` returns `null` (no filtering). Without a custom provider, all rows are visible to all principals.
- The resource advisor pipeline calls `IAccessProvider` at Order = 100,000,000. Advisors at lower Order values run before the access check.

## See also

- [Built-in Features](core/built-in-features.md) — feature priority table
- [Advice Pipeline](core/advice-pipeline.md) — how advisors short-circuit
- [Create Pipeline](resource/create-pipeline.md) — authorization lane in the resource pipeline
- [Identity](identity.md) — ASP.NET Core Identity integration
- [Authorization](authorization.md) — OAuth 2.0 / OIDC server
