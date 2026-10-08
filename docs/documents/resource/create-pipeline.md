# Create Pipeline

A Create request enters the dispatcher as `CreateResourceRequest<TEntity,TRequest,TDetail>` and returns `CreateResultBase<TDetail>`. The dispatcher wraps the Resource handler, so envelope-wide policy runs before handler-stage entity work.

## Wrap stages

The ordered wrap chain performs these steps:

1. Authentication checks a non-anonymous caller when `WithAuthentication()` registered the closed advisor.
2. Coarse authorization checks the Create permission when `WithAuthorization()` registered it.
3. The sanitize wrap clears server-managed fields from `TRequest`.
4. The validation wrap runs `IValidationAdvisor<TRequest>` when the stage is installed; `WithoutCreateValidation()` excludes it at registration, and `RepositoryBase.SuppressAddValidation()` skips it for one operation.
5. The idempotency wrap replays a finalized AIP-155 result or reserves its key.
6. The handler maps, persists, and returns its result.
7. The detail-response wrap derives a child parent and obtains an ETag.
8. The idempotency wrap commits the shaped detail.

The sanitizer clears `Name`, `CanonicalName`, `Timestamp`, `EntityTag`, `Uid`, `Owner`, `State`, `CreateTime`, `UpdateTime`, `DeleteTime`, and `PurgeTime` when the request exposes those properties. The request reference reaches the handler after sanitization.

## Handler stages

The handler maps `TRequest` to `TEntity`; a null mapping throws `ValidationException` with `INVALID_PAYLOAD`. It applies `IChild.Parent` to the entity's structural parent fields before running `IResourceCreateAdvisor<TEntity,TRequest>`, so instance access sees the parent used for persistence. Update's create-on-missing branch uses the same sequence. The selected domain mutation owner or repository persists the entity, then the handler maps it to `TDetail`.

Instance access receives the mapped entity and the Create request so an overridden access provider can evaluate both. Create has no row query to which entitlement can apply.
## Idempotency

The idempotency wrap derives its key from `IRequestIdentification.RequestId`, `Create`, entity type, principal, and target; the payload hash travels with the cached value. A finalized record with a matching hash returns `CreateResultBase<TDetail>` without invoking the handler. A finalized record or pending reservation carrying a different hash fails validation on `request_id` with `REQUEST_ID_PAYLOAD_MISMATCH`. A matching pending reservation waits for completion up to `SchemataResourceOptions.IdempotencyPendingWait`, then throws `AbortedException`. Reservation data is local to the wrap invocation; the ambient context carries only pipeline markers such as suppression.

## Extension points

- Implement `IRequestPipelineAdvisor<CreateResourceRequest<TEntity,TRequest,TDetail>,CreateResultBase<TDetail>>` for envelope-wide behavior.
- Implement `IResourceCreateAdvisor<TEntity,TRequest>` for policy requiring the mapped entity.
- Implement `IValidationAdvisor<TRequest>` for validation collection.
- Register advisors with `TryAddEnumerable`.

## See also

- [Resource overview](overview.md)
- [Update pipeline](update-pipeline.md)
- [Security](../security.md)
