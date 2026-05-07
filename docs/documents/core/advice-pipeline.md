# Advice Pipeline

The advice pipeline is Schemata's mechanism for injecting cross-cutting concerns into
operations at well-defined interception points. Advisors are small, focused units of
logic that run in sequence before, during, or after an operation. Each advisor can
inspect and modify state, allow the operation to continue, or short-circuit the pipeline
entirely.

## IAdvisor

All advisors implement the marker interface `IAdvisor`, which defines a single property:

```csharp
public interface IAdvisor
{
    int Order { get; }
}
```

`Order` controls the sequence in which advisors execute within a pipeline. Lower values
run first.

### Generic variants

The generic `IAdvisor<T1>` through `IAdvisor<T1, ..., T16>` interfaces extend `IAdvisor`
and declare the `AdviseAsync` method. The arity of the generic interface matches the
number of arguments the advisor receives alongside the `AdviceContext`:

```csharp
public interface IAdvisor<in T1> : IAdvisor
{
    Task<AdviseResult> AdviseAsync(AdviceContext ctx, T1 a1, CancellationToken ct = default);
}

public interface IAdvisor<in T1, in T2> : IAdvisor
{
    Task<AdviseResult> AdviseAsync(AdviceContext ctx, T1 a1, T2 a2, CancellationToken ct = default);
}

// ... up to IAdvisor<T1, T2, ..., T16>
```

Every type parameter is declared `in` (contravariant), so an advisor registered against
a base type will match pipelines that pass a derived type.

## AdviceContext

`AdviceContext` is a typed property bag that flows through the entire pipeline, giving
advisors shared state and access to the service provider.

```csharp
public class AdviceContext
{
    public AdviceContext(IServiceProvider sp);

    public IServiceProvider ServiceProvider { get; }

    public void Set<T>(T? value);
    public T?   Get<T>();
    public bool TryGet<T>(out T? value);
    public bool Has<T>();
}
```

Internally, values are keyed by `RuntimeTypeHandle`, so each type can store exactly one
value. This makes `AdviceContext` a lightweight alternative to passing many parameters
through the pipeline.

| Method                 | Behavior                                                                                 |
| ---------------------- | ---------------------------------------------------------------------------------------- |
| `Set<T>(value)`        | Stores `value` keyed by `typeof(T)`. Overwrites any previous value of the same type.     |
| `Get<T>()`             | Returns the stored value or throws `KeyNotFoundException`.                               |
| `TryGet<T>(out value)` | Returns `true` and sets `value` when a non-null entry exists; otherwise returns `false`. |
| `Has<T>()`             | Returns `true` if an entry for `typeof(T)` exists (even if the value is null).           |

The `ServiceProvider` property allows advisors to resolve additional services at
execution time without constructor injection.

## AdviseResult

Every `AdviseAsync` call returns one of three `AdviseResult` values that control pipeline
flow:

```csharp
public enum AdviseResult
{
    Continue = 0,
    Block    = 1,
    Handle   = 2,
}
```

| Result     | Meaning                                                                                                                                                                                                                              |
| ---------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `Continue` | The pipeline proceeds to the next advisor. If this is the last advisor, the operation executes normally.                                                                                                                             |
| `Block`    | The pipeline stops immediately. The operation is denied. Callers typically treat this as a silent refusal or return a default value.                                                                                                 |
| `Handle`   | The pipeline stops immediately. The advisor has fully handled the operation (for example, returning a cached result or converting a delete into a soft-delete update). Callers use whatever state the advisor placed in the context. |

Both `Block` and `Handle` short-circuit the pipeline -- no further advisors run, and
the normal operation logic is skipped. The distinction between the two lets callers
decide how to interpret the early exit.

## Pipeline execution

### AdviceRunner

The `AdviceRunner<TAdvisor, T1, ..., TN>` static classes contain the actual execution
loop. There is one class per arity, matching the `IAdvisor` generic variants. Every
runner follows the same algorithm:

```csharp
public static async Task<AdviseResult> RunAsync(AdviceContext ctx, T1 a1, CancellationToken ct = default)
{
    var advisors = ctx.ServiceProvider.GetServices<TAdvisor>().OrderBy(a => a.Order).ToList();
    foreach (var advisor in advisors)
    {
        ct.ThrowIfCancellationRequested();
        var result = await advisor.AdviseAsync(ctx, a1, ct);
        if (result is not AdviseResult.Continue)
        {
            return result;
        }
    }

    return AdviseResult.Continue;
}
```

Key points:

1. **Resolution** -- advisors are resolved from DI via `GetServices<TAdvisor>()`, which
   returns all registrations for the advisor interface.
2. **Ordering** -- the resolved advisors are sorted by `Order` ascending. Advisors with
   the same `Order` value run in registration order.
3. **Short-circuiting** -- the loop stops on the first non-`Continue` result and
   propagates it to the caller.
4. **Cancellation** -- `CancellationToken` is checked before each advisor executes.

### Advisor entry point and AdvicePipeline\<TAdvisor\>

Callers create a pipeline through the `Advisor` static class:

```csharp
public static class Advisor
{
    public static AdvicePipeline<TAdvisor> For<TAdvisor>() where TAdvisor : IAdvisor
        => default;
}
```

`AdvicePipeline<TAdvisor>` is a zero-size struct used purely as a dispatch token for
extension methods. It carries no state. The source generator (described below) emits
`RunAsync` extension methods on this struct, so the call site reads naturally:

```csharp
var result = await Advisor.For<IRepositoryAddAdvisor<Student>>()
                          .RunAsync(AdviceContext, repository, entity, ct);
```

### How callers consume the result

Repository and resource operations typically switch on the result:

```csharp
switch (await Advisor.For<IRepositoryAddAdvisor<TEntity>>()
                     .RunAsync(AdviceContext, this, entity, ct))
{
    case AdviseResult.Block:
    case AdviseResult.Handle:
        return;
    case AdviseResult.Continue:
    default:
        break;
}

// Normal operation proceeds here
```

Some pipelines interpret the three results differently. For example, query pipelines
treat `Handle` as "use the result already placed in the context" and `Block` as
"return the default value."

## Source generator

The `Schemata.Advice.Generator` package contains an incremental source generator
(`AdvicePipelineGenerator`) that eliminates the need to write boilerplate `RunAsync`
extension methods by hand.

### How it works

1. **Candidate detection** -- the generator scans for interface declarations whose base
   list contains `IAdvisor`. This is a fast syntactic check on `InterfaceDeclarationSyntax`.

2. **Semantic analysis** -- for each candidate, it resolves the `IAdvisor<...>` base
   interface to extract the type arguments. It also captures any type parameters and
   constraints declared on the advisor interface itself.

3. **Code emission** -- for each advisor interface, it emits a partial class
   `AdvicePipelineExtensions` with a `RunAsync` extension method on
   `AdvicePipeline<TAdvisor>`. The extension method delegates to the corresponding
   `AdviceRunner<TAdvisor, T1, ..., TN>.RunAsync(...)`.

For example, given:

```csharp
public interface IRepositoryAddAdvisor<TEntity> : IAdvisor<IRepository<TEntity>, TEntity>
    where TEntity : class;
```

The generator emits (simplified):

```csharp
public static partial class AdvicePipelineExtensions
{
    public static Task<AdviseResult> RunAsync<TEntity>(
        this AdvicePipeline<IRepositoryAddAdvisor<TEntity>> _,
        AdviceContext ctx,
        IRepository<TEntity> a1,
        TEntity a2,
        CancellationToken ct = default)
        where TEntity : class
        => AdviceRunner<IRepositoryAddAdvisor<TEntity>, IRepository<TEntity>, TEntity>
            .RunAsync(ctx, a1, a2, ct);
}
```

The generator activates only when `Schemata.Advice.AdvicePipeline<T>` is available in
the compilation, ensuring it does not emit code into projects that lack the runtime
infrastructure.

## Categories of advisors

### Repository advisors

Repository advisors participate in data-access pipelines. They are defined in
`Schemata.Entity.Repository` and receive repository-level arguments such as the
`IRepository<TEntity>` instance and the entity being operated on.

| Interface                                       | Base                                          | When it runs                                                            |
| ----------------------------------------------- | --------------------------------------------- | ----------------------------------------------------------------------- |
| `IRepositoryAddAdvisor<TEntity>`                | `IAdvisor<IRepository<TEntity>, TEntity>`     | Before an entity is inserted.                                           |
| `IRepositoryUpdateAdvisor<TEntity>`             | `IAdvisor<IRepository<TEntity>, TEntity>`     | Before an entity is updated.                                            |
| `IRepositoryRemoveAdvisor<TEntity>`             | `IAdvisor<IRepository<TEntity>, TEntity>`     | Before an entity is deleted.                                            |
| `IRepositoryBuildQueryAdvisor<TEntity>`         | `IAdvisor<QueryContainer<TEntity>>`           | During query construction, before the user predicate is applied.        |
| `IRepositoryQueryAdvisor<TEntity, TResult, T>`  | `IAdvisor<QueryContext<TEntity, TResult, T>>` | After the query is built but before it executes against the data store. |
| `IRepositoryResultAdvisor<TEntity, TResult, T>` | `IAdvisor<QueryContext<TEntity, TResult, T>>` | After query execution, allowing post-processing of the result.          |

For more detail on how these advisors compose within a mutation, see
[Mutation Pipeline](../repository/mutation-pipeline.md).

### Resource advisors

Resource advisors run during HTTP/gRPC resource operations. They are defined in
`Schemata.Resource.Foundation` and typically receive a `ClaimsPrincipal?` as their last
argument.

**General (cross-operation) advisors:**

| Interface                                    | Base                                             | When it runs                                                           |
| -------------------------------------------- | ------------------------------------------------ | ---------------------------------------------------------------------- |
| `IResourceRequestAdvisor<TEntity>`           | `IAdvisor<ClaimsPrincipal?, Operations>`         | First in every resource operation (List, Get, Create, Update, Delete). |
| `IResourceResponseAdvisor<TEntity, TDetail>` | `IAdvisor<TEntity?, TDetail?, ClaimsPrincipal?>` | After an entity is mapped to a detail DTO (Get, Create, Update).       |

**List-specific advisors:**

| Interface                                | Base                                                                         | When it runs                                    |
| ---------------------------------------- | ---------------------------------------------------------------------------- | ----------------------------------------------- |
| `IResourceListRequestAdvisor<TEntity>`   | `IAdvisor<ListRequest, ResourceRequestContainer<TEntity>, ClaimsPrincipal?>` | After the general request advisor, during List. |
| `IResourceListResponseAdvisor<TSummary>` | `IAdvisor<ImmutableArray<TSummary>?, ClaimsPrincipal?>`                      | After query execution and mapping to summaries. |

**Get-specific advisors:**

| Interface                             | Base                                                                        | When it runs                                   |
| ------------------------------------- | --------------------------------------------------------------------------- | ---------------------------------------------- |
| `IResourceGetRequestAdvisor<TEntity>` | `IAdvisor<GetRequest, ResourceRequestContainer<TEntity>, ClaimsPrincipal?>` | After the general request advisor, during Get. |

**Create-specific advisors:**

| Interface                                          | Base                                                                      | When it runs                               |
| -------------------------------------------------- | ------------------------------------------------------------------------- | ------------------------------------------ |
| `IResourceCreateRequestAdvisor<TEntity, TRequest>` | `IAdvisor<TRequest, ResourceRequestContainer<TEntity>, ClaimsPrincipal?>` | Before the request is mapped to an entity. |
| `IResourceCreateAdvisor<TEntity, TRequest>`        | `IAdvisor<TRequest, TEntity, ClaimsPrincipal?>`                           | After mapping, before persistence.         |

**Update-specific advisors:**

| Interface                                          | Base                                                                      | When it runs                                               |
| -------------------------------------------------- | ------------------------------------------------------------------------- | ---------------------------------------------------------- |
| `IResourceUpdateRequestAdvisor<TEntity, TRequest>` | `IAdvisor<TRequest, ResourceRequestContainer<TEntity>, ClaimsPrincipal?>` | Before the entity is modified.                             |
| `IResourceUpdateAdvisor<TEntity, TRequest>`        | `IAdvisor<TRequest, TEntity, ClaimsPrincipal?>`                           | After the request advisor, before mapping onto the entity. |

**Delete-specific advisors:**

| Interface                                | Base                                                                           | When it runs                                                   |
| ---------------------------------------- | ------------------------------------------------------------------------------ | -------------------------------------------------------------- |
| `IResourceDeleteRequestAdvisor<TEntity>` | `IAdvisor<DeleteRequest, ResourceRequestContainer<TEntity>, ClaimsPrincipal?>` | Before the entity is deleted.                                  |
| `IResourceDeleteAdvisor<TEntity>`        | `IAdvisor<TEntity, DeleteRequest, ClaimsPrincipal?>`                           | After the request advisor, before removal from the repository. |

### Validation advisors

Defined in `Schemata.Validation.Skeleton`:

| Interface               | Base                                                  | When it runs                                                                                               |
| ----------------------- | ----------------------------------------------------- | ---------------------------------------------------------------------------------------------------------- |
| `IValidationAdvisor<T>` | `IAdvisor<Operations, T, IList<ErrorFieldViolation>>` | During validation. Receives the CRUD operation kind, the object being validated, and a mutable error list. |

### Authorization advisors

Authorization advisors are defined in `Schemata.Authorization.Skeleton.Advisors`. Built-in implementations are registered automatically by `UseAuthorization()` and individual flow features (`UseCodeFlow()`, `UseDeviceFlow()`, etc.). For architectural details, see [Authorization](../authorization.md).

**Token endpoint** -- runs once for all grant types before the grant-specific handler:

| Interface              | Base                                                        | When it runs                                               |
| ---------------------- | ----------------------------------------------------------- | ---------------------------------------------------------- |
| `ITokenRequestAdvisor` | `IAdvisor<TokenRequest, Dictionary<string, List<string>>?>` | Client authentication, grant permission, scope validation. |

**Authorization Code flow** -- enabled by `UseCodeFlow()`:

| Interface                   | Base                                                                           | When it runs                                                                                          |
| --------------------------- | ------------------------------------------------------------------------------ | ----------------------------------------------------------------------------------------------------- |
| `IAuthorizeEndpointAdvisor` | `IAdvisor<AuthorizationRequestContext>`                                        | Authorization endpoint: client lookup, redirect URI, PKCE, scope, prompt, consent, and auto-approval. |
| `ICodeExchangeAdvisor`      | `IAdvisor<SchemataApplication, TokenRequest, AuthorizeRequest, SchemataToken>` | Token endpoint: PKCE verification and custom hooks during authorization code exchange.                |

**Device Authorization flow** -- enabled by `UseDeviceFlow()`:

| Interface                    | Base                                                    | When it runs                                             |
| ---------------------------- | ------------------------------------------------------- | -------------------------------------------------------- |
| `IDeviceAuthorizeAdvisor`    | `IAdvisor<SchemataApplication, DeviceAuthorizeRequest>` | After client auth, before device/user code generation.   |
| `IDeviceCodeExchangeAdvisor` | `IAdvisor<SchemataApplication?, SchemataToken>`         | Token endpoint: device code validation and status check. |

**Refresh Token flow** -- enabled by `UseRefreshTokenFlow()`:

| Interface              | Base                                            | When it runs                                                             |
| ---------------------- | ----------------------------------------------- | ------------------------------------------------------------------------ |
| `IRefreshTokenAdvisor` | `IAdvisor<SchemataApplication?, SchemataToken>` | Token endpoint: refresh token validation, expiry, client ownership.      |
| `IRefreshScopeAdvisor` | `IAdvisor<RefreshScopeContext>`                 | Ensures requested scope is a subset of the original grant (RFC 6749 §6). |

**Token Exchange** -- automatically enabled by `UseCodeFlow()` and `UseDeviceFlow()`:

| Interface               | Base                                           | When it runs                                 |
| ----------------------- | ---------------------------------------------- | -------------------------------------------- |
| `ITokenExchangeAdvisor` | `IAdvisor<SchemataApplication, SchemataToken>` | Validates token status and grant permission. |

**Token Introspection (RFC 7662)** -- enabled by `UseIntrospection()`:

| Interface                      | Base                                                             | When it runs                           |
| ------------------------------ | ---------------------------------------------------------------- | -------------------------------------- |
| `IIntrospectionRequestAdvisor` | `IAdvisor<IntrospectRequest, Dictionary<string, List<string>>?>` | Client authentication.                 |
| `IIntrospectionAdvisor`        | `IAdvisor<IntrospectionResponse>`                                | Customizes the introspection response. |

**Token Revocation (RFC 7009)** -- enabled by `UseRevocation()`:

| Interface                   | Base                                                         | When it runs                                   |
| --------------------------- | ------------------------------------------------------------ | ---------------------------------------------- |
| `IRevocationRequestAdvisor` | `IAdvisor<RevokeRequest, Dictionary<string, List<string>>?>` | Client authentication.                         |
| `IRevocationAdvisor`        | `IAdvisor<SchemataApplication?, SchemataToken>`              | Per-token hook before revocation is committed. |

**End Session (RP-Initiated Logout)** -- enabled by `UseEndSession()`:

| Interface                   | Base                                              | When it runs                                                   |
| --------------------------- | ------------------------------------------------- | -------------------------------------------------------------- |
| `IEndSessionRequestAdvisor` | `IAdvisor<EndSessionRequest>`                     | Validates id_token_hint, resolves client, checks redirect URI. |
| `IEndSessionAdvisor`        | `IAdvisor<SchemataApplication?, ClaimsPrincipal>` | After validation, before sign-out.                             |

**Claims and token issuance** -- registered by `UseAuthorization()`:

| Interface             | Base                                                | When it runs                                                  |
| --------------------- | --------------------------------------------------- | ------------------------------------------------------------- |
| `IClaimsAdvisor`      | `IAdvisor<List<Claim>>`                             | Claims enrichment before token issuance.                      |
| `IDestinationAdvisor` | `IAdvisor<Claim, HashSet<string>, ClaimsPrincipal>` | Determines which tokens (access/identity) receive each claim. |
| `ITokenAdvisor`       | `IAdvisor<SchemataApplication?, ClaimsPrincipal>`   | Final adjustments before token issuance.                      |

**Discovery and UserInfo** -- registered by `UseAuthorization()`:

| Interface                  | Base                                   | When it runs                                                                   |
| -------------------------- | -------------------------------------- | ------------------------------------------------------------------------------ |
| `IDiscoveryAdvisor`        | `IAdvisor<DiscoveryDocument>`          | Populates the OIDC discovery document. Each flow feature adds its own entries. |
| `IUserInfoRequestAdvisor`  | `IAdvisor<ClaimsPrincipal>`            | Validates the access token at the UserInfo endpoint.                           |
| `IUserInfoResponseAdvisor` | `IAdvisor<Dictionary<string, object>>` | Customizes the UserInfo response body.                                         |

## Registering custom advisors

Advisors are registered in the DI container using `TryAddEnumerable` with a
`ServiceDescriptor.Scoped` descriptor. This pattern ensures that each implementation
type is registered at most once (no duplicates) while allowing multiple different
advisor types for the same interface.

### Closed-type registration

When the advisor is a concrete (non-generic) class targeting a specific entity:

```csharp
services.TryAddEnumerable(
    ServiceDescriptor.Scoped<IRepositoryAddAdvisor<Student>, StudentNameAdvisor>());
```

### Open-generic registration

When the advisor applies to all entities (or all entities matching a constraint):

```csharp
services.TryAddEnumerable(
    ServiceDescriptor.Scoped(typeof(IRepositoryAddAdvisor<>), typeof(AdviceAddConcurrency<>)));
```

### Example: implementing a custom advisor

```csharp
internal sealed class StudentNameAdvisor : IRepositoryAddAdvisor<Student>
{
    public int Order => 0;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext         ctx,
        IRepository<Student> repository,
        Student              entity,
        CancellationToken    ct)
    {
        if (string.IsNullOrWhiteSpace(entity.Name))
        {
            entity.Name = $"students/{Guid.NewGuid():N}";
        }

        return Task.FromResult(AdviseResult.Continue);
    }
}
```

Register it during startup:

```csharp
services.TryAddEnumerable(
    ServiceDescriptor.Scoped<IRepositoryAddAdvisor<Student>, StudentNameAdvisor>());
```

## Pipeline short-circuiting

Short-circuiting is the defining flow-control feature of the advice pipeline.

When any advisor returns `Block` or `Handle`, the runner immediately returns that result
without calling the remaining advisors. The caller then decides how to react based on
which result it received.

Because advisors are sorted by `Order`, lower-order advisors act as gatekeepers for
higher-order ones. The framework uses `SchemataConstants.Orders` anchors for built-in
advisor ordering:

| Anchor      | Value       | Usage                                                                                            |
| ----------- | ----------- | ------------------------------------------------------------------------------------------------ |
| `Base`      | 100,000,000 | Starting point for most built-in advisors (authorization, idempotency, timestamps, freshness)    |
| `Extension` | 400,000,000 | Starting point for extension feature advisors                                                    |
| `Max`       | 900,000,000 | Terminal advisors (soft-delete conversion, concurrency validation, response idempotency storage) |

Built-in advisors chain by adding `10_000_000` increments from an anchor. For example, the create-request advisor chain: sanitize at `Base` (100M), idempotency at `Base + 10M` (110M), anonymous access at `Base + 20M` (120M), authorization at `Base + 30M` (130M), validation at `Base + 40M` (140M). Custom advisors can use any value that falls between the built-in increments or outside the reserved range.
