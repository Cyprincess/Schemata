# Create Pipeline

A Create request enters the dispatcher as `CreateResourceRequest<TEntity,TRequest,TDetail>` and returns `CreateResultBase<TDetail>`. The dispatcher wraps the Resource handler, so envelope-wide policy runs before handler-stage entity work.

## Wrap stages

The ordered wrap chain performs these steps:

1. Authentication checks a non-anonymous caller when `WithAuthentication()` registered the closed advisor.
2. Coarse authorization checks the Create permission when `WithAuthorization()` registered it.
3. The sanitize wrap clears server-managed fields from `TRequest`.
4. The validation wrap runs `IValidationAdvisor<TRequest>` unless `CreateRequestValidationSuppressed` is present.
5. The idempotency wrap replays a finalized AIP-155 result or reserves its key.
6. The handler maps, persists, and returns its result.
7. The detail-response wrap derives a child parent and obtains an ETag.
8. The idempotency wrap commits the shaped detail.

The sanitizer clears `Name`, `CanonicalName`, `Timestamp`, `EntityTag`, `Uid`, `Owner`, `State`, `CreateTime`, `UpdateTime`, `DeleteTime`, and `PurgeTime` when the request exposes those properties. The request reference reaches the handler after sanitization.

## Handler stages

The handler maps `TRequest` to `TEntity`; a null mapping throws `ValidationException` with `INVALID_PAYLOAD`. `IResourceCreateAdvisor<TEntity,TRequest>` runs after mapping. `AdviceApplyChildParent` derives a mode-A parent field from the request parent when applicable. The repository adds and commits the entity, then the handler maps it to `TDetail`.

Instance access receives the mapped entity and the Create request so an overridden access provider can evaluate both. Create has no row query to which entitlement can apply.
## Idempotency

The idempotency wrap derives its key from `IRequestIdentification.RequestId`, `Create`, entity type, principal, target, and payload hash. A finalized matching record returns `CreateResultBase<TDetail>` without invoking the handler. A pending record waits for completion or throws `AbortedException`. Reservation data is local to the wrap invocation; the ambient context carries only pipeline markers such as suppression.

## Extension points

- Implement `IRequestPipelineAdvisor<CreateResourceRequest<TEntity,TRequest,TDetail>,CreateResultBase<TDetail>>` for envelope-wide behavior.
- Implement `IResourceCreateAdvisor<TEntity,TRequest>` for policy requiring the mapped entity.
- Implement `IValidationAdvisor<TRequest>` for validation collection.
- Register advisors with `TryAddEnumerable`.

## See also

- [Resource overview](overview.md)
- [Update pipeline](update-pipeline.md)
- [Security](../security.md)
