# Create Pipeline

The create operation is handled by `ResourceOperationHandler.CreateAsync`. It takes a `TRequest` DTO and produces a `CreateResult<TDetail>`. The method runs through the full [advisor pipeline](../core/advice-pipeline.md) with five stages before returning the created resource.

## Pipeline Steps

### 1. General Request Advisor

```
IResourceRequestAdvisor<TEntity> -- Operations.Create
```

The first gate runs for every operation type. It receives the `HttpContext` and the `Operations.Create` enum value. Returns `Continue` to proceed, `Handle` to short-circuit with a `CreateResult<TDetail>` stored in the context, or `Block` to silently deny.

### 2. Request Sanitization

Before the create-specific advisors run, the handler clears identity fields that clients must not set:

- `request.Name` is set to `null`.
- `request.CanonicalName` is set to `null`.
- If the request implements `IIdentifier`, `request.Id` is reset to `default` (0).

This ensures that server-generated identifiers and names are never influenced by client input.

### 3. Create Request Advisors

```
IResourceCreateRequestAdvisor<TEntity, TRequest>
```

Multiple advisors run in order. The built-in ones, from lowest to highest order:

| Order       | Advisor                          | Behavior                                                         |
| ----------- | -------------------------------- | ---------------------------------------------------------------- |
| 100,000,000 | `AdviceCreateRequestIdempotency` | Checks idempotency store for a cached result.                    |
| 110,000,000 | `AdviceCreateRequestAuthorize`   | Checks authorization (only if `WithAuthorization()` was called). |
| 120,000,000 | `AdviceCreateRequestValidation`  | Validates the request through `IValidationAdvisor<TRequest>`.    |

#### Idempotency (AIP-155)

When the request implements `IRequestIdentification` and provides a non-null `RequestId`:

1. `AdviceCreateRequestIdempotency` looks up the `RequestId` in the `IIdempotencyStore`.
2. If a cached `CreateResult<TDetail>` exists, it is placed in the `AdviceContext` and the advisor returns `Handle`, short-circuiting the entire pipeline. The client receives the same response as the original request.
3. If no cached result exists, a `PendingIdempotencyKey` is stored in the `AdviceContext` for later use by `AdviceResponseIdempotency`.

The idempotency check can be suppressed by placing a `SuppressCreateIdempotency` marker in the `AdviceContext`.

The default `IdempotencyStore` is backed by `IDistributedCache` with a 24-hour absolute expiration. It serializes results to JSON via `System.Text.Json`.

#### Authorization

`AdviceCreateRequestAuthorize` is only registered when `WithAuthorization()` is called on the builder. It:

1. Checks if the entity type has `[Anonymous(Operations.Create)]` -- if so, skips authorization.
2. Otherwise calls `IAccessProvider<TEntity, ResourceRequestContext<TRequest>>.HasAccessAsync` with the current `ClaimsPrincipal`.
3. Throws `AuthorizationException` if access is denied.

#### Validation

`AdviceCreateRequestValidation` delegates to `IValidationAdvisor<TRequest>` implementations. If validation fails, it throws `ValidationException` with field violations.

When the request implements `IValidation` and `ValidateOnly` is `true`, the advisor throws `NoContentException` after validation to signal a dry-run -- no entity is created.

Validation can be suppressed globally via `WithoutCreateValidation()` on the builder, or by placing a `SuppressCreateRequestValidation` marker in the context. Even when suppressed, a `ValidateOnly` request still throws `NoContentException`.

### 4. Entity Mapping and Parent Resolution

After the request advisors pass:

1. The handler maps `TRequest` to `TEntity` via `ISimpleMapper.Map<TRequest, TEntity>`. If mapping returns null, it throws `InvalidArgumentException`.
2. If an `HttpContext` is available, parent properties are set on the entity from route values using `ResourceNameDescriptor.SetParentFromRouteValues`. This populates parent foreign keys (e.g., `PublisherName`) from the URL path segments.

### 5. Create Entity Advisor

```
IResourceCreateAdvisor<TEntity, TRequest>
```

This advisor has access to both the original request and the mapped entity. It runs after parent properties have been set. Custom advisors can inspect or modify the entity before persistence.

### 6. Persistence

The entity is added to the repository and committed:

```csharp
await _repository.AddAsync(entity, ct);
await _repository.CommitAsync(ct);
```

### 7. Response Mapping and Advisors

The persisted entity is mapped to `TDetail` via `ISimpleMapper.Map<TEntity, TDetail>`.

```
IResourceResponseAdvisor<TEntity, TDetail>
```

Two built-in response advisors run:

| Order       | Advisor                     | Behavior                                                                                                           |
| ----------- | --------------------------- | ------------------------------------------------------------------------------------------------------------------ |
| 100,000,000 | `AdviceResponseFreshness`   | Sets the ETag on the detail if the entity implements `IConcurrency` and the detail implements `IFreshness`.        |
| 900,000,000 | `AdviceResponseIdempotency` | If a `PendingIdempotencyKey` exists in the context, stores the `CreateResult<TDetail>` in the `IIdempotencyStore`. |

### 8. Result

A `CreateResult<TDetail>` is returned with the `Detail` property populated. In the [HTTP transport](./http-transport.md), this becomes a 201 Created response with a `Location` header pointing to the new resource.

## Implementing a Custom Create Advisor

To add custom logic, implement one of the advisor interfaces and register it:

```csharp
public class SetCreateTimestamp<TEntity, TRequest> : IResourceCreateAdvisor<TEntity, TRequest>
    where TEntity : class, ICanonicalName, ITimestamp
    where TRequest : class, ICanonicalName
{
    public int Order => 150_000_000;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext ctx, TRequest request, TEntity entity,
        HttpContext? http, CancellationToken ct = default)
    {
        entity.CreateTime = DateTime.UtcNow;
        return Task.FromResult(AdviseResult.Continue);
    }
}
```
