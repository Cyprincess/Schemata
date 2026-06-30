# Security

`Schemata.Security.Foundation` enforces access at two points: an `IAccessProvider<T, TRequest>`
gate that decides whether a principal may perform an operation, and an
`IEntitlementProvider<T, TRequest>` that narrows a repository query to the rows the principal may
see. `UseSecurity()` registers the feature and the default providers; the resource pipeline calls
them through the authorize advisors that `WithAuthorization()` installs.

## Where the code lives

| Package                        | Key files                                                                                                                                                           |
| ------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Schemata.Security.Skeleton`   | `IAccessProvider.cs`, `IEntitlementProvider.cs`, `IPermissionResolver.cs`, `IPermissionMatcher.cs`, `AccessContext.cs`, `AnonymousAccess.cs`, `AnonymousGranted.cs` |
| `Schemata.Security.Foundation` | `Extensions/SchemataBuilderExtensions.cs` (`UseSecurity`), `Features/SchemataSecurityFeature.cs`, `SchemataSecurityOptions.cs`                                      |
| `Schemata.Security.Foundation` | `DefaultAccessProvider.cs`, `DefaultEntitlementProvider.cs`, `DefaultPermissionResolver.cs`, `DefaultPermissionMatcher.cs`                                          |
| `Schemata.Resource.Foundation` | `Advisors/Advice{List,Get,Create,Update,Delete}RequestAuthorize.cs`, `SchemataResourceBuilder.WithAuthorization`                                                    |

## The two providers

Both interfaces live in `Schemata.Security.Skeleton` and are open generic over the entity type
`T` and the request DTO `TRequest`. Both receive an `AccessContext<TRequest>` carrying the
operation name and the request payload:

```csharp
public class AccessContext<TRequest>
{
    public string?  Operation { get; set; }   // "List", "Get", "Create", "Update", "Delete"
    public TRequest? Request  { get; set; }
}
```

`IAccessProvider<T, TRequest>` returns a yes/no decision:

```csharp
Task<bool> HasAccessAsync(
    T?                      entity,
    AccessContext<TRequest> context,
    ClaimsPrincipal?        principal,
    CancellationToken       ct = default);
```

`IEntitlementProvider<T, TRequest>` returns a predicate, or `null` for no filtering:

```csharp
Task<Expression<Func<T, bool>>?> GenerateEntitlementExpressionAsync(
    AccessContext<TRequest> context,
    ClaimsPrincipal?        principal,
    CancellationToken       ct = default);
```

## Enabling the feature

```csharp
builder.UseSchemata(schema => {
    schema.UseSecurity();
    schema.UseSecurity(o => o.PermissionClaimType = "permissions"); // override the claim type
});
```

`Microsoft.AspNetCore.Builder.SchemataBuilderExtensions.UseSecurity` stores the optional
`Action<SchemataSecurityOptions>` and calls `builder.AddFeature<SchemataSecurityFeature>()`.

`SchemataSecurityFeature` has `Priority = Orders.Extension = 400_000_000` and no declared
dependencies. `ConfigureServices` registers four services, all with `TryAddScoped`:

```csharp
services.TryAddScoped<IPermissionResolver, DefaultPermissionResolver>();
services.TryAddScoped<IPermissionMatcher, DefaultPermissionMatcher>();
services.TryAddScoped(typeof(IAccessProvider<,>), typeof(DefaultAccessProvider<,>));
services.TryAddScoped(typeof(IEntitlementProvider<,>), typeof(DefaultEntitlementProvider<,>));
```

Replacement works differently per shape. For `IPermissionResolver` and `IPermissionMatcher`, a
custom registration wins wherever it lands: added before the feature, the feature's `TryAdd`
becomes a no-op; added after, the later descriptor wins single resolution. For the two
open-generic providers, a closed-generic registration such as
`IAccessProvider<Student, StudentRequest>` always takes precedence — the container matches the
exact constructed type before falling back to the open generic — and the open-generic default
fills in every other entity.

## Default access decision

`DefaultAccessProvider<T, TRequest>` is a claims-based gate composing `IPermissionResolver` and
`IPermissionMatcher`:

1. An unauthenticated principal (`principal?.Identity?.IsAuthenticated != true`) is denied.
2. A missing operation name is denied.
3. Otherwise it resolves a permission string and asks the matcher whether the principal holds it.

`DefaultPermissionResolver.Resolve(operation, entity)` produces `{entity}.{operation}` in
kebab-case via Humanizer's `Kebaberize()`. `Resolve("Create", typeof(OrderItem))` returns
`"order-item.create"`.

`DefaultPermissionMatcher` reads claims of type `SchemataSecurityOptions.PermissionClaimType`
(default `"role"`) and matches the resolved permission against them:

- An exact string match succeeds.
- A claim may contain a single `*` wildcard segment. The claim and permission must have the same
  number of dot-separated segments; each non-wildcard segment must match positionally.
- The wildcard may not be the first segment when the permission has more than two segments, and a
  claim with two or more wildcards never matches.

So `student.*` grants every operation on `Student`, and `*.list` grants `List` on any single-word
entity, while a bare `*` matches nothing.

| Claim            | Matches `student.create`? |
| ---------------- | ------------------------- |
| `student.create` | yes (exact)               |
| `student.*`      | yes                       |
| `*.create`       | yes                       |
| `*`              | no                        |
| `student.*.*`    | no (two wildcards)        |

## Default entitlement

`DefaultEntitlementProvider<T, TRequest>.GenerateEntitlementExpressionAsync` returns `null`, so no
row-level filter is applied. A custom provider returns an `Expression<Func<T, bool>>` to narrow
results, or `null` to opt out for a given request.

## How the resource pipeline invokes security

The security providers are dormant until the resource builder opts an entity in:

```csharp
schema.UseResource()
      .WithAuthorization()
      .MapHttp()
      .Use<Student, StudentRequest, StudentDetail, StudentSummary>();
```

`SchemataResourceBuilder.WithAuthorization(string? scheme = null)` registers two advisor families
per operation via `TryAddEnumerable`:

| Advisor family                                                 | Order                       | Role                                                                                                         |
| -------------------------------------------------------------- | --------------------------- | ------------------------------------------------------------------------------------------------------------ |
| `Advice{List,Get,Create,Update,Delete,Method}RequestAnonymous` | `Orders.Base` = 100,000,000 | Stashes `AnonymousGranted` in `AdviceContext` when the entity's `[Anonymous]` attribute covers the operation |
| `Advice{List,Get,Create,Update,Delete,Method}RequestAuthorize` | 110,000,000                 | Calls the access and entitlement providers                                                                   |

Each authorize advisor builds an `AccessContext<TRequest>` with the operation name, then:

1. Calls `IEntitlementProvider.GenerateEntitlementExpressionAsync` and passes the result to
   `container.ApplyModification(expression)`. A non-null predicate becomes a `.Where(...)` on the
   composed query, narrowing the result set at the data layer. Entitlement filtering runs on List,
   Get, Update, and Delete regardless of the anonymous marker; Create has no entitlement step.
2. Skips the access check when `AdviceContext.Has<AnonymousGranted>()`; otherwise calls
   `AuthorizeHelper.EnsureAsync(access, context, parent, principal, ct)`, which throws
   `PermissionDeniedException` (HTTP 403) when access is denied.

`AnonymousGranted` is set by the anonymous advisors, which consult `AnonymousAccess.IsAnonymous`.
That helper reads the `[Anonymous]` attribute on the entity: no operation list grants all
operations; a list grants only the named ones (case-insensitive).

If the resource is mapped without `WithAuthorization()`, neither advisor family is registered and
the providers are never called.

## Extension points

| Interface                           | Purpose                                                                                                               |
| ----------------------------------- | --------------------------------------------------------------------------------------------------------------------- |
| `IAccessProvider<T, TRequest>`      | Decide whether a principal may run an operation. A closed-generic registration overrides the default for that entity. |
| `IEntitlementProvider<T, TRequest>` | Return a query predicate for row-level filtering. Same registration pattern.                                          |
| `IPermissionResolver`               | Map operation + entity type to a permission string.                                                                   |
| `IPermissionMatcher`                | Decide whether a principal holds a resolved permission.                                                               |

`SchemataSecurityOptions` carries one property, `PermissionClaimType` (default `"role"`), used by
`DefaultPermissionMatcher`.

## Design rationale

The split is deliberate: the access provider answers "may this principal touch this kind of thing
at all", while the entitlement provider answers "which specific rows". The entitlement predicate
runs in the database, so a List that the principal is allowed to call still returns only the rows
they own. Replacing `DefaultAccessProvider` with a domain check and `DefaultEntitlementProvider`
with an ownership predicate covers most row-level security needs without touching the resource
pipeline.

## Caveats

- The defaults deny unauthenticated callers and require a matching permission claim. Mapping an
  entity with `WithAuthorization()` and no claims yields 403 until the principal carries the right
  permission or the entity is marked `[Anonymous]`.
- `DefaultPermissionResolver` kebab-cases the bare type name, not the namespace. Two entities with
  the same short name resolve to the same permission.
- A `*` wildcard claim only matches permissions with the same segment count. `student.*` does not
  match a three-segment permission.

## See also

- [Access Control](../guides/access-control.md) — a worked roles-and-row-level-security setup
- [Built-in Features](core/built-in-features.md) — feature priority table
- [Ownership](repository/ownership.md) — `UseOwner()` for automatic ownership filtering
