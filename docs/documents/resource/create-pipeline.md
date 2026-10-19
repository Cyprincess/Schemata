# Create Pipeline

A Create request enters the dispatcher as `CreateResourceRequest<TEntity,TRequest,TDetail>` and returns `CreateResultBase<TDetail>`. The dispatcher wraps the Resource handler, so envelope-wide policy runs before handler-stage entity work.

## Wrap stages

The ordered wrap chain performs these steps:

1. Authentication checks a non-anonymous caller when `WithAuthentication()` registered the closed advisor.
2. Coarse authorization checks the Create permission when `WithAuthorization()` registered it.
3. The sanitize wrap clears server-managed fields from `TRequest`.
4. The validation wrap runs `IValidationAdvisor<TRequest>` when the stage is installed; `WithoutCreateValidation()` excludes it at registration.
5. The idempotency wrap replays a finalized AIP-155 result or reserves its key.
6. The handler maps, persists, and returns its result.
7. The detail-response wrap derives a child parent and obtains an ETag.
8. The idempotency wrap commits the shaped detail.

The sanitizer clears `Name`, `CanonicalName`, `Timestamp`, `EntityTag`, `Uid`, `Owner`, `State`, `CreateTime`, `UpdateTime`, `DeleteTime`, and `PurgeTime` when the request exposes those properties. The request reference reaches the handler after sanitization.

## Handler stages

The handler maps `TRequest` to `TEntity`; a null mapping throws `ValidationException` with `INVALID_PAYLOAD`. It applies `IChild.Parent` to the entity's structural parent fields before running `IResourceCreateAdvisor<TEntity,TRequest,TDetail>`, so instance access sees the parent used for persistence. Update's create-on-missing branch uses the same sequence. The selected domain mutation owner or repository persists the entity, then the handler maps it to `TDetail`. An advisor-blocked or advisor-handled mutation without a write returns `MutationResult.NoWrite`, so the handler answers with a `Detail` of `null` and the transports respond `204 No Content` (HTTP) or a model-created default detail (gRPC).

The gRPC unary adapter converts the null detail to a non-null `TDetail` by deserializing an empty
protobuf payload through the configured runtime model. ASP.NET Core gRPC requires a non-null unary
response before marshalling; the adapter preserves the registered resource response type.

Instance access receives the mapped entity and the Create request so an overridden access provider can evaluate both. Create has no row query to which entitlement can apply.
## Idempotency

The idempotency wrap derives its key from `IRequestIdentification.RequestId`, `Create`, entity type, principal, and target; the payload hash travels with the cached value. A finalized record with a matching hash returns `CreateResultBase<TDetail>` without invoking the handler. A finalized record or pending reservation carrying a different hash fails validation on `request_id` with `REQUEST_ID_PAYLOAD_MISMATCH`. A matching pending reservation waits for completion up to `SchemataResourceOptions.IdempotencyPendingWait`, then throws `AbortedException`. Reservation data is local to the wrap invocation. Registration selects whether the wrap is installed; cache identity and serialized payloads stay in the reservation and request objects, not in `AdviceContext`. Settlement follows the dispatch report rather than the response shape: a create whose mutation reports `MutationResult.NoWrite` marks the dispatch `NotWritten` and answers a null `Detail`, and the wrap releases that reservation so a retry re-runs the create deterministically. A committed create can never answer a null `Detail` because the handler maps its detail through `RequireDetail`, which throws on an unmapped detail.

## Extension points

- Implement `IRequestPipelineAdvisor<CreateResourceRequest<TEntity,TRequest,TDetail>,CreateResultBase<TDetail>>` for envelope-wide behavior.
- Implement `IResourceCreateAdvisor<TEntity,TRequest,TDetail>` for policy requiring the mapped entity. Each standard stage receives a `HandledResponse<TResult>` for its operation result; an advisor that short-circuits writes it and returns `AdviseResult.Handle`.
- Implement `IValidationAdvisor<TRequest>` for validation collection.
- Register advisors with `TryAddEnumerable`.

## See also

- [Resource overview](overview.md)
- [Update pipeline](update-pipeline.md)
- [Security](../security.md)
