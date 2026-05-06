# Update Pipeline

The update operation is handled by `ResourceOperationHandler.UpdateAsync`. It takes a `TRequest` DTO and an existing `TEntity`, applies changes through the [advisor pipeline](../core/advice-pipeline.md), and returns an `UpdateResult<TDetail>`.

## Pipeline Steps

### 1. General Request Advisor

```
IResourceRequestAdvisor<TEntity> -- Operations.Update
```

The first gate. Returns `Continue` to proceed, `Handle` to short-circuit with an `UpdateResult<TDetail>` from the context, or `Block` to silently deny.

### 2. Update Request Advisors

```
IResourceUpdateRequestAdvisor<TEntity, TRequest>
```

Multiple advisors run in order:

| Order       | Advisor                                | Behavior                                                                    |
| ----------- | -------------------------------------- | --------------------------------------------------------------------------- |
| 100,000,000 | `AdviceUpdateRequestSanitize`          | Silently clears server-managed fields on the request and strips them from the update mask. |
| 110,000,000 | `AdviceUpdateRequestAnonymous`         | Sets `AnonymousGranted` if the entity allows anonymous update.                             |
| 120,000,000 | `AdviceUpdateRequestAuthorize`         | Checks authorization (only if `WithAuthorization()` was called).                           |
| 130,000,000 | `AdviceUpdateRequestValidation`        | Validates the request through `IValidationAdvisor<TRequest>`.                              |

`AdviceUpdateRequestSanitize` runs first at `Orders.Base`. Like the create sanitizer, it clears nine server-managed fields by property name: `Name`, `Timestamp`, `Id`, `Owner`, `State`, `CreateTime`, `UpdateTime`, `DeleteTime`, and `PurgeTime`. Unlike the create variant, it also **strips these fields from the `IUpdateMask`** if the request implements `IUpdateMask` with a non-null mask. This prevents a client from bypassing field-level sanitization by setting `update_mask=owner` while the payload field was cleared — since partial update logic merges the mask into the entity, not the payload.

#### Authorization

`AdviceUpdateRequestAuthorize` is only registered when `WithAuthorization()` is called. It:

1. Checks if the entity type has `[Anonymous(Operations.Update)]` -- if so, skips authorization.
2. Calls `IAccessProvider<TEntity, TRequest>.HasAccessAsync` with the current `ClaimsPrincipal`.
3. Throws `AuthorizationException` if access is denied.

#### Validation

`AdviceUpdateRequestValidation` delegates to `IValidationAdvisor<TRequest>` implementations. If validation fails, it throws `ValidationException` with field violations.

When the request implements `IValidation` and `ValidateOnly` is `true`, the advisor throws `NoContentException` after validation to signal a dry-run -- no update is performed.

Validation can be suppressed globally via `WithoutUpdateValidation()` on the builder, or by placing a `SuppressUpdateRequestValidation` marker in the context.

### 3. Update Entity Advisor

```
IResourceUpdateAdvisor<TEntity, TRequest>
```

This advisor has access to both the request and the existing entity. It runs before the request fields are mapped onto the entity.

The built-in advisor at this stage:

| Order       | Advisor                 | Behavior                                             |
| ----------- | ----------------------- | ---------------------------------------------------- |
| 100,000,000 | `AdviceUpdateFreshness` | Enforces optimistic concurrency via ETag comparison. |

#### Freshness Validation

`AdviceUpdateFreshness` enforces optimistic concurrency when both conditions are met:

1. The entity implements `IConcurrency` and has a non-empty `Timestamp`.
2. The request implements `IFreshness` and provides an `EntityTag` that starts with `W/`.

When both are present, the advisor computes the expected ETag from the entity's `IConcurrency.Timestamp` (a `Guid` converted to a Base64 URL-safe weak ETag: `W/"<base64>"`) and compares it with the request's `EntityTag`. A mismatch throws `ConcurrencyException`.

When the request does not carry an ETag, the check is silently skipped -- this allows clients that do not care about concurrency to omit it.

Freshness can be suppressed globally via `WithoutFreshness()` on the builder, which places a `SuppressFreshness` marker in the `AdviceContext`.

### 4. Field Mask Mapping

The handler applies the request fields to the entity using one of two strategies:

#### With Update Mask (IUpdateMask)

When the request implements `IUpdateMask` and `UpdateMask` is non-null, only the specified fields are mapped. The mask is a comma-separated list of snake_case field paths (following the AIP field mask convention). The handler:

1. Splits the mask on commas and trims whitespace.
2. Converts each field name from snake_case to PascalCase using `Pascalize()`.
3. Filters to only fields that exist as properties on `TEntity`.
4. Calls `ISimpleMapper.Map(request, entity, fields)` to update only those properties.

Note that `AdviceUpdateRequestSanitize` (step 2) already stripped the system-managed fields from the mask, so no server fields appear here.

This enables partial updates where only the fields listed in the mask are touched.

#### Without Update Mask

When no update mask is provided, the handler calls `ISimpleMapper.Map(request, entity)` which maps all non-null properties from the request onto the entity. Since identity and server-managed fields were already cleared by `AdviceUpdateRequestSanitize`, they will not overwrite the entity's values.

### 5. Persistence

The entity is updated in the repository and committed:

```csharp
await _repository.UpdateAsync(entity, ct);
await _repository.CommitAsync(ct);
```

### 6. Response Mapping and Advisors

The updated entity is mapped to `TDetail` via `ISimpleMapper.Map<TEntity, TDetail>`.

```
IResourceResponseAdvisor<TEntity, TDetail>
```

Built-in response advisors:

| Order       | Advisor                     | Behavior                                                                                                            |
| ----------- | --------------------------- | ------------------------------------------------------------------------------------------------------------------- |
| 100,000,000 | `AdviceResponseFreshness`   | Sets the updated ETag on the detail if the entity implements `IConcurrency` and the detail implements `IFreshness`. |
| 900,000,000 | `AdviceResponseIdempotency` | Only stores results for create operations (no-op for update).                                                       |

### 7. Result

An `UpdateResult<TDetail>` is returned with the `Detail` property populated. In the [HTTP transport](./http-transport.md), this becomes a 200 OK response with the updated detail as JSON.

## Concurrency Flow Summary

The typical concurrency-safe update flow:

1. Client fetches the resource via GET and receives an ETag in the response.
2. Client sends a PATCH request with the ETag (either in the request body via `IFreshness.EntityTag`, the `etag` query parameter, or the `If-Match` HTTP header).
3. The handler resolves the entity by name.
4. `AdviceUpdateFreshness` compares the request ETag with the current entity timestamp.
5. If they match, the update proceeds. If not, a `ConcurrencyException` is thrown (HTTP 409 Conflict).

## Implementing a Custom Update Advisor

To add custom logic before the entity is modified:

```csharp
public class AuditUpdate<TEntity, TRequest> : IResourceUpdateAdvisor<TEntity, TRequest>
    where TEntity : class, ICanonicalName, ITimestamp
    where TRequest : class, ICanonicalName
{
    public int Order => 150_000_000;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext ctx, TRequest request, TEntity entity,
        ClaimsPrincipal? principal, CancellationToken ct = default)
    {
        entity.UpdateTime = DateTime.UtcNow;
        return Task.FromResult(AdviseResult.Continue);
    }
}
```
