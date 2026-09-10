# Security

`UseSecurity()` registers the default permission resolver, permission matcher, access provider, and entitlement provider. A domain builder opts into request authentication and authorization with the shared `IResourceBuilder` extensions from `Schemata.Security.Foundation`.

## Packages

| Package | Role |
| --- | --- |
| `Schemata.Security.Skeleton` | Security contracts, `SecurityOrders`, `AnonymousAccess`, and `ResourceSecurityRegistration` |
| `Schemata.Security.Foundation` | Default providers, dispatcher-wrap authentication and authorization advisors, and shared builder extensions |
| domain Foundation package | Registers the closed advisors and handler-stage access and entitlement advisors for its request envelopes |

## Enabling security

Call `UseSecurity()` to register the default services. Call the two extensions independently on an external domain builder:

```csharp
builder.UseSchemata(schema => {
    schema.UseSecurity();
    schema.UseResource()
          .WithAuthentication("Bearer")
          .WithAuthorization()
          .MapHttp()
          .Use<Student, StudentRequest, StudentDetail, StudentSummary>();
});
```

`WithAuthentication<TBuilder>(TBuilder, string?)` records the transport scheme through that builder's `ResourceSecurityRegistration` and registers its closed authentication advisors. `WithAuthorization<TBuilder>(TBuilder)` registers the corresponding coarse dispatcher advisors and domain handler advisors. Both return the same builder type. A builder without a registration throws `InvalidOperationException` rather than silently configuring another domain.

Each domain builder stores a `ResourceSecurityRegistration` in `SchemataOptions`. Its three delegates register authentication, register authorization, and store an optional scheme. The shared extensions call those delegates without inspecting the service collection. `SchemataResourceBuilder`, Flow, Report, Scheduling, Insight, and the Identity and Authorization management builders implement `IResourceBuilder`.

## Three authorization responsibilities

Security assigns work according to the information an advisor needs.

| Responsibility | Pipeline position | Behavior |
| --- | --- | --- |
| Authentication | `IRequestPipelineAdvisor<TRequest,TResponse>` before continuation | Checks `Principal.Identity.IsAuthenticated` for a non-anonymous operation and throws `UnauthenticatedException`. |
| Coarse authorization | `IRequestPipelineAdvisor<TRequest,TResponse>` after authentication and before continuation | Resolves an operation and entity permission through `IPermissionResolver`, matches it through `IPermissionMatcher`, and throws the result of the AIP-211 existence probe. |
| Instance authorization and entitlement | Domain handler stages | Calls `IAccessProvider<TEntity,TRequest>` with the loaded entity or `null` for Create, and applies an `IEntitlementProvider<TEntity,TRequest>` expression to query containers. |

Both wrap advisors call `AnonymousAccess.IsAnonymous(entity, operation)` themselves. An anonymous operation therefore bypasses authentication and coarse authorization without a marker passed from another advisor. Handler-stage access checks also skip anonymous operations, while entitlement predicates still apply to anonymous queries.

`SecurityOrders` fixes the outer chain order: `Authentication`, `Authorization`, `Sanitize`, `Validation`, `Idempotency`, then the response family. Before segments run in ascending order. After segments unwind in reverse order.

Authentication and coarse authorization are independently enabled. Authentication throws `UNAUTHENTICATED` for a non-anonymous caller without an authenticated identity. Coarse authorization uses `IPermissionResolver` and `IPermissionMatcher`. A denied Get returns `NOT_FOUND`; a denied Update or Delete returns `PERMISSION_DENIED` only when the principal matches the corresponding Get permission, otherwise `NOT_FOUND`. Create, List, and method operations return `PERMISSION_DENIED` on a coarse denial.

`SchemataSecurityFeature` registers `IPermissionResolver`, `IPermissionMatcher`, `IAccessProvider<,>`, and `IEntitlementProvider<,>` with `TryAddScoped`.

`DefaultPermissionResolver` creates a kebab-case `{entity}.{operation}` permission. `DefaultPermissionMatcher` matches claims of `SchemataSecurityOptions.PermissionClaimType`, which defaults to `role`; it supports one wildcard segment subject to its segment-count checks.

The default access provider is the instance-level implementation. Coarse authorization uses the resolver and matcher directly. Replace the resolver or matcher to customize coarse permissions. Register a closed `IAccessProvider<TEntity,TRequest>` to customize instance authorization for one resource, or replace the open generic to change the default. An entitlement provider returns an expression or `null`; domain request stages apply a returned expression to the repository query.

## Transport and management surfaces

A transport scheme populates the request principal. The authentication wrap advisor is the single `IsAuthenticated` decision point. `MapHttp()` and `MapGrpc()` are domain-specific extensions that activate their domain feature; shared transport behavior is declared through those features' dependencies.

Identity User and Role management resources and Authorization Application, Scope, and Token management resources require an explicit `MapHttp()` or `MapGrpc()` call. The IdentityCore endpoints and Authorization protocol endpoints retain their own transport paths.

## Stored resource identity

The host owns `SchemataSecurity.Name` and persisted `SchemataToken.Name`. Supply names explicitly or
register a consumer `IRepositoryAddAdvisor<TEntity>` before `AdviceAddCanonicalName.DefaultOrder`
(120,000,000), using [the repository naming pattern](repository/mutation-pipeline.md#consumer-owned-resource-names).
Framework-created security rows and repository-backed token slots require that same policy. A blank
required `Name` fails canonical-name resolution with `ValidationException`; the framework does not
supply a fallback. `CanonicalName` derives from `Name` using `securities/{security}` or
`tokens/{token}`.

`SchemataSecurity.Key` is a parent-scoped credential label. The `(Parent, Key)` index is separate
from resource identity. `ISecurityStore<TSecurity>` retains canonical-name lookup and parent listing
with optional kind, usage, and status filters. `SecurityStore<TSecurity>` orders parent listings by
descending `CreateTime`, then `Key`. Adding the label field does not add a select-by-key API or
change which credential kinds and statuses an authentication consumer accepts. `Kid` remains a
separate key-material identifier.

`SchemataToken.Key` is the semantic slot identifier used by `GetAsync`, `GetOrCreateAsync`,
`SetAsync`, and `RemoveAsync` with `Parent` and `Provider`. Repository slots have a unique
`(Parent, Provider, Key)` index; token resource `Name` has an independent unique index.
`FindByNameAsync` addresses the resource name, not the slot key. Cache-backed slots return `Key`
without generating `Name` and do not run repository advisors.

Existing deployments require a schema and data migration: copy old slot names and credential labels
into their respective `Key` fields, update the associated indexes, and supply independent resource
names while preserving or migrating canonical references. Authorization applications also require
an independent `Name` while retaining their protocol `ClientId`. Schemata provides no automatic
migration or compatibility alias. See the [migration requirements](authorization.md#required-schema-and-data-migration).

The contracts and indexes are in `src/Schemata.Security.Skeleton/Entities/SchemataSecurity.cs`,
`src/Schemata.Security.Skeleton/Entities/SchemataToken.cs`, and
`src/Schemata.Security.Skeleton/Services/ISecurityStore.cs`. The executable store behavior is in
`src/Schemata.Security.Foundation/Stores/{SecurityStore,RepositoryTokenStore,CacheTokenStore}.cs`.

## See also

- [Resource overview](resource/overview.md)
- [Messaging](messaging/overview.md)
- [Access Control](../guides/access-control.md)
