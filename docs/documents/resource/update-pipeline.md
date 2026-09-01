# Update Pipeline

An Update request enters the dispatcher as `UpdateResourceRequest<TEntity,TRequest,TDetail>` and returns `UpdateResultBase<TDetail>`. The dispatcher runs envelope-wide stages before the handler loads the target entity.

## Wrap stages

`SecurityOrders` fixes this chain: authentication, coarse authorization, sanitize, validation, idempotency, response shaping. Before segments run in that order. The response detail is shaped before the idempotency wrap commits its cache record.

The sanitize wrap clears the same server-managed fields as Create. The validation wrap runs `IValidationAdvisor<TRequest>` unless `UpdateRequestValidationSuppressed` is present. The idempotency wrap replays a completed response for a matching request ID and payload hash or reserves a pending record.

## Handler stages

The handler binds the URI name to the request and applies its name predicates to a `ResourceRequestContainer<TEntity>`. Entitlement advisors add any row predicate to that container. The handler loads the entity under soft-delete suppression.

`IResourceUpdateAdvisor<TEntity,TRequest>` runs after the entity loads. The instance access advisor calls `IAccessProvider<TEntity,TRequest>` with that entity and the update request. `AdviceUpdateSoftDeleted` rejects an update of a tombstoned entity. `AdviceUpdateFreshness` compares a supplied ETag to the entity tag and throws `AbortedException` for a mismatch.

The mapper applies every field when the update mask is absent or `*`; otherwise it maps the selected wire paths. It persists and commits the entity, maps `TDetail`, and returns. The response wrap derives `IChild.Parent` from the detail canonical name and requests an ETag from `IEntityTagProvider` when applicable.

## Security behavior

Coarse authorization uses the Update verb and entity type before the load. Its probe returns `PERMISSION_DENIED` only when the principal matches the corresponding Get permission; otherwise it returns `NOT_FOUND`. Instance access remains a handler-stage check because it needs the loaded entity.

## Extension points

- Implement `IRequestPipelineAdvisor<UpdateResourceRequest<TEntity,TRequest,TDetail>,UpdateResultBase<TDetail>>` for envelope-wide behavior.
- Implement `IResourceUpdateAdvisor<TEntity,TRequest>` for mapped entity behavior.
- Implement `IResourceUpdateRequestAdvisor<TEntity,TRequest>` for container-scoped request behavior.

## See also

- [Resource overview](overview.md)
- [Create pipeline](create-pipeline.md)
- [Delete pipeline](delete-pipeline.md)
