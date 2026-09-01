# Read Pipeline

List and Get requests enter the dispatcher as `ListResourceQueryRequest<TEntity,TSummary>` and `GetResourceQueryRequest<TEntity,TDetail>`. The dispatcher wraps each handler with the registered Resource and Security advisors.

Authentication and coarse authorization execute before a handler runs when their domain builder enabled the corresponding shared extension. A denied Get returns `NOT_FOUND`; List returns `PERMISSION_DENIED` on a coarse denial.


Handler stages retain data-dependent work. Entitlement advisors apply expressions to a `ResourceRequestContainer<TEntity>`. Get access checks run after the target entity is loaded; List access has no entity and uses the request and a null entity. Anonymous operations skip authentication and access checks while entitlement filtering still applies.

## List

The handler applies parent scoping, page-token validation, filters, ordering, and total-size selection to its container. It fetches one extra row to compute `next_page_token`, maps summaries, and returns `ListResultBase<TSummary>`.

The list response wrap then derives `IChild.Parent` from each summary canonical name. Schemata returns complete summaries and has no partial-response trimming stage.

## Get

The handler binds the request name, loads under soft-delete suppression, and throws `ResourceNotFound` when absent. It maps the entity to `TDetail` and returns `GetResultBase<TDetail>`.

The detail response wrap derives `IChild.Parent` from the mapped detail and sets `IFreshness.EntityTag` through `IEntityTagProvider` unless freshness is suppressed. It operates after the handler maps the detail, so the provider reads the detail rather than requiring the entity.

## Extension points

- Implement `IResourceListRequestAdvisor<TEntity>` or `IResourceGetRequestAdvisor<TEntity>` to add container predicates.
- Implement `IResourceGetAdvisor<TEntity>` for post-load Get policy.
- Implement a closed `IRequestPipelineAdvisor<...>` to wrap the complete List or Get envelope.

## See also

- [Resource overview](overview.md)
- [Filtering](filtering.md)
- [Security](../security.md)
