# Delete Pipeline

A Delete request enters the dispatcher as `DeleteResourceRequest<TEntity,TDetail>` and returns `DeleteResultBase<TDetail>`. Authentication and coarse authorization wrap the handler; entity-dependent checks stay inside it.

## Request and entity stages

The handler binds the URI name into a `ResourceRequestContainer<TEntity>`. Handler-stage entitlement adds a predicate. It loads the target under soft-delete suppression. A missing target throws `ResourceNotFound` unless `AllowMissing` is true.

After the load, the instance access advisor evaluates `IAccessProvider<TEntity,TRequest>` with the entity. `AdviceDeleteFreshness` validates an ETag when the request supplies one. A mismatch throws `AbortedException` with `CONCURRENCY_MISMATCH`.

The handler removes and commits the entity. A soft-delete repository advisor writes `DeleteTime`; a hard delete has no detail. The detail response wrap shapes a soft-delete detail by deriving `IChild.Parent` and creating an ETag when the response implements the corresponding traits.

## Authorization

Authentication and coarse authorization are dispatcher-wrap advisors. They run before name lookup. The coarse Delete probe follows the same AIP-211 parent-read behavior as Get and Update. The handler-stage access check remains after the load because a provider may inspect the concrete entity.

## Built-in soft-delete methods

`SchemataResourceFeature.RegisterResource` adds `undelete`, `expunge`, and `purge` custom methods for an `ISoftDelete` resource when its operation list permits them. Each method dispatches through the method envelope, so its verb is visible to wrap security and idempotency policy.

| Method | Route |
| --- | --- |
| `undelete` | `POST /v1/{collection}/{name}:undelete` |
| `expunge` | `POST /v1/{collection}/{name}:expunge` |
| `purge` | `POST /v1/{collection}:purge` |

## Extension points

- Implement `IResourceDeleteRequestAdvisor<TEntity>` for query-container policy.
- Implement `IResourceDeleteAdvisor<TEntity>` for loaded entity behavior.
- Implement a closed `IRequestPipelineAdvisor<DeleteResourceRequest<TEntity,TDetail>,DeleteResultBase<TDetail>>` for envelope-wide policy.

## See also

- [Resource overview](overview.md)
- [Update pipeline](update-pipeline.md)
- [Custom methods](custom-methods.md)
