# Read Pipeline

List and Get requests enter the dispatcher as `ListResourceQueryRequest<TEntity,TSummary>` and `GetResourceQueryRequest<TEntity,TDetail>`. The dispatcher wraps each handler with the registered Resource and Security advisors.

Authentication and coarse authorization execute before a handler runs when their domain builder enabled the corresponding shared extension. A denied Get returns `NOT_FOUND`; List returns `PERMISSION_DENIED` on a coarse denial.


Handler stages retain data-dependent work. Entitlement advisors apply expressions to a `ResourceRequestContainer<TEntity>`. Get access checks run after the target entity is loaded; List access has no entity and uses the request and a null entity. Anonymous operations skip authentication and access checks while entitlement filtering still applies.

## List

The handler applies parent scoping, page-token validation, filters, ordering, and total-size selection to its container. It fetches one extra row to compute `next_page_token`, maps summaries, and returns `ListResultBase<TEntity, TSummary>`.

The list response wrap then derives `IChild.Parent` from each summary canonical name. Schemata returns complete summaries and has no partial-response trimming stage.

### Continuation tokens

Resource List uses `Schemata.Common.ProtectedContinuation` to stream internal JSON through Brotli,
protect the compressed payload with ASP.NET DataProtection, and encode it as Base64 URL text. Its
purpose remains `Schemata.Resource.Foundation.PageToken`. The payload retains filter, language,
order-by, parent, show-deleted, page size, and skip; changing the first five request parameters
rejects the continuation with a `page_token` field violation. The payload also binds the issuing
tenant and caller identity; replaying the token under a different tenant or principal fails with
the same violation.

A continuation without `page_size` retains the token's effective size under the current maximum.
An explicit zero selects the configured default, and a larger explicit size clamps to the maximum.
An explicit skip is added to the continuation offset. Negative decoded positions and offset
arithmetic overflow raise `INVALID_ARGUMENT`; the existing request-skip normalization still clamps
a resulting negative request offset to zero. Invalid protection, encoding, compression, and JSON
also produce the canonical page-token field violation.

Resource installs DataProtection through its service registration. A persistent key ring and a
shared application discriminator preserve continuations across host restarts and instances. The
codec adds no token-size or decompressed-size policy.

Implementation: `src/Schemata.Resource.Foundation/Models/PageToken.cs` and
`src/Schemata.Resource.Foundation/ResourceOperationHandler.List.cs`.

## Get

The handler binds the request name, loads under soft-delete suppression, and throws `ResourceNotFound` when absent. It maps the entity to `TDetail` and returns `GetResultBase<TDetail>`.

The detail response wrap derives `IChild.Parent` from the mapped detail and sets `IFreshness.EntityTag` through the registered `IEntityTagProvider`; `WithoutFreshness()` removes the default provider, so no tag is emitted unless the host registers its own. It operates after the handler maps the detail, so the provider reads the detail rather than requiring the entity.

## Extension points

- Implement `IResourceListRequestAdvisor<TEntity>` or `IResourceGetRequestAdvisor<TEntity>` to add container predicates.
- Implement `IResourceGetAdvisor<TEntity>` for post-load Get policy.
- Implement a closed `IRequestPipelineAdvisor<...>` to wrap the complete List or Get envelope.

## See also

- [Resource overview](overview.md)
- [Filtering](filtering.md)
- [Security](../security.md)
