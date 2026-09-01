# Resource API Interactions and Google AIPs

This reference is for Schemata maintainers and application developers designing resource operations. It compares published [Google API Improvement Proposals](https://google.aip.dev/) with the executable Schemata resource, HTTP JSON, and gRPC surfaces.

## Reading the status labels

| Status | Meaning in this reference |
| --- | --- |
| Enforced | The default runtime path directly checks or generates the behavior. |
| Supported by extension point | Schemata supplies the integration point; the application supplies the resource-specific semantics. |
| Application responsibility | The application must design or validate the behavior. |
| Partial | Schemata implements part of the AIP; the missing requirements are stated beside the status. |
| Not implemented | The runtime has no corresponding handler, route, serializer, descriptor, or registration surface. |
| Not applicable | The requirement does not apply to the stated Schemata surface. |

Schemata is code-first. CLR DTOs, advisor stages, mappings, and repository entities are internal shapes. They are not `google.api` annotations or canonical protobuf request messages. The transport adapters determine the published contract.

## Method selection

[AIP-130](https://google.aip.dev/130) orders operation choices as standard methods, standard batch or aggregate methods, custom methods, then streaming methods. Schemata exposes standard CRUD and resource custom methods; it does not select a category for an application.

| AIP | Status | Framework boundary | Application decision |
| --- | --- | --- | --- |
| [AIP-130](https://google.aip.dev/130) | Application responsibility | `ResourceAttribute.Operations` registers allowed standard operations; `ResourceMethodAttribute` registers custom verbs. | Prefer a standard operation when its semantics fit. Select a custom method only for an independent action, and choose streaming outside the resource surface. |
| [AIP-127](https://google.aip.dev/127) | Partial | HTTP controller and gRPC service conventions register routes and unary RPCs from resource metadata. | The generated endpoints use code-first conventions rather than `google.api.http` annotations, additional bindings, or `google.api.method_signature` annotations. |
| [AIP-136](https://google.aip.dev/136) | Partial | `ResourceMethodAttribute`, resource registration, `ResourceMethodOperationHandler`, `ResourceMethodControllerConvention`, and `ResourceCustomMethod` create one HTTP and gRPC surface for resource- and collection-scoped handlers. | Choose a verb+noun name, request/response DTOs, read-only `GET` or mutating `POST`, and the correct instance or collection scope. Stateless custom methods have no Schemata resource scope and require an application-owned endpoint. |

The framework does not create stateless custom methods. `ResourceMethodAttribute` is attached to a resource and supports only `Instance` and `Collection` scopes. A stateless operation therefore belongs to an application-owned transport and handler surface.

## Standard and custom operation wire contract

The following table traces the shipped operation path. “Bare” means the transport removes an internal result wrapper before serializing. A resource must be registered through `Use<...>()` or `AddResource<T>()`; attributes alone do not publish an HTTP controller, gRPC service, descriptor, or custom RPC.

| Operation | Internal request and transformation | HTTP JSON wire | gRPC protobuf wire | Transport and AIP limit |
| --- | --- | --- | --- | --- |
| List | `ListResourceQueryRequest` runs dispatcher wraps, then the handler resolves the parent, compiles the filter, orders, paginates the query, maps summaries, and list response wraps shape `ListResultBase<TSummary>`. | `GET /v1/{collection}` with query-bound `parent`, `page_size`, `page_token`, `skip`, `filter`, `language`, `order_by`, and `show_deleted`. The result object remains wrapped. | Unary `List{Plural}(ListRequest)` returns `ListResultBase<TSummary>`. | `Entities` is rewritten to the resource plural on both wires. The request is a framework DTO, not a canonical `List{Plural}Request` with `google.api` annotations. |
| Get | `GetResourceQueryRequest` runs dispatcher wraps; the handler loads the entity through container policy and maps the detail; response wraps apply the weak ETag into `GetResultBase<TDetail>`. | `GET /v1/{collection}/{name}` returns bare `TDetail`. | Unary `Get{Singular}(GetRequest)` returns bare `TDetail`. | The exposed request shape is framework-defined rather than a generated AIP request message. |
| Create | `CreateResourceRequest` wraps sanitize and validate the payload; the handler applies `AdviceApplyChildParent`, maps `TRequest` to `TEntity`, stages `AddAsync`, commits, and maps the detail; idempotency and response wraps produce `CreateResultBase<TDetail>`. | `POST /v1/{collection}` binds a bare `TRequest` body, returns `201 Created` and bare `TDetail`, and sets `Location`. | Unary `Create{Singular}(TRequest)` returns bare `TDetail`. | Parent data is filled from route values. The wire has no canonical `{resource}` wrapper or required `{resource}_id` field. |
| Update | `UpdateResourceRequest` wraps sanitize and validate; the handler puts the URI name into `request.CanonicalName`, clears parent properties, loads the entity, runs update entity advisors (soft-deleted rejection, freshness), maps all fields or `IUpdateMask.UpdateMask`, commits, and maps the detail. `IAllowMissing` routes a missing entity through the create path. | `PATCH /v1/{collection}/{name}` binds bare `TRequest`, returns bare `TDetail`, and can read `etag` from query or `If-Match`. | Unary `Update{Singular}(TRequest)` returns bare `TDetail`; the request carries `name`, `etag`, and `update_mask` when its DTO implements the corresponding traits. | The request is the resource DTO, not an AIP `Update{Resource}Request` containing a nested resource field. `update_mask` is a string trait rather than `google.protobuf.FieldMask`. |
| Delete | `DeleteResourceRequest` runs dispatcher-wrap policy, then handler container and entity stages, removes, commits, and returns `DeleteResultBase<TDetail>`. | `DELETE /v1/{collection}/{name}?etag=&allow_missing=`. A soft delete returns `200` and bare detail; a hard delete returns `204`. | Unary `Delete{Singular}(DeleteRequest)` returns `TDetail?` for soft deletion or `google.protobuf.Empty` for hard deletion. | There is no built-in child-resource existence check or force cascade. |
| Custom method | `ResourceMethodOperationHandler` creates a `ResourceMethodRequest` carrying the verb, target, payload, and principal. Dispatcher wraps run before Resource method handler stages and the inner handler. | Instance: `POST /v1/{collection}/{name}:{verb}`. Collection: `POST /v1/{collection}:{verb}`. A declared read-only method binds from query on `GET`; other requests bind as bare JSON bodies. | A unary `{Verb}{Singular}` RPC is registered on the resource service. | Custom method envelopes make per-verb security and idempotency available before handler work. |
| Purge | A collection custom method serializes `PurgeOperationArgs`, triggers `PurgeJob<TEntity>`, and returns a Schemata `Operation`; the scheduled job recompiles the filter and produces `PurgeResponse`. | Collection custom-method route with `:purge`. `OperationResponse.Output` is structured JSON. | Unary custom RPC. `OperationResponse.Output` remains a string. | Schemata `Operation` is not automatically `google.longrunning.Operation`; the HTTP and gRPC operation payload contracts differ. |

`SchemataJsonTraits` hides `ICanonicalName.Name`, exposes `CanonicalName` as `name`, exposes `IFreshness.EntityTag` as `etag`, and changes `IEntitiesResult<T>.Entities` to the resource plural. The base HTTP serializer applies snake_case property names, kebab-case enums, string encoding for `long`, and null omission. `SchemataProtoModelConfigurator` applies the shared trait names, snake_case protobuf names, and proto3 map configuration to registered request, detail, summary, list-result, and custom-method types. A scalar map with a null value is written as a key-only protobuf entry and is read as an empty string.

The two adapters are registration boundaries. A resource mapped only with `MapHttp()` has no gRPC service or descriptor. A resource mapped only with `MapGrpc()` has no MVC endpoint. An application that replaces serializer options can change the HTTP serializer behavior outside `SchemataJsonTraits`.

## Standard methods

### Get and List

[AIP-131](https://google.aip.dev/131) requires a Get method that returns a single resource. [AIP-132](https://google.aip.dev/132) requires List for non-singletons and specifies a parent, pagination, plural response field, and no request body.

| AIP | Status | Implemented behavior | Gap or application responsibility |
| --- | --- | --- | --- |
| [AIP-131](https://google.aip.dev/131) | Partial | `GetAsync` is registered as HTTP `GET` and unary gRPC `Get{Singular}`; both return a bare detail DTO after the resource pipeline. | The public request is `GetRequest`, and the HTTP name is a controller route segment. Schemata emits no `google.api.http`, field behavior, resource-reference, or method-signature annotations. |
| [AIP-132](https://google.aip.dev/132) | Partial | `ListAsync` enforces negative `page_size` as validation failure, defaults zero or omitted size to 25, caps size at 100, validates token-bound filter/parent/language/order fields, and emits `next_page_token` only when a look-ahead row finds more results. `total_size` follows `TotalSizeMode`: `Exact` by default, `Estimated` through `EstimateCountAsync`, and `None` omits the field; the mode is configured globally through `SchemataResourceOptions.TotalSize` or per resource through `ResourceAttribute.TotalSize`, and residual evaluation computes the total after the residual runs. | The result field is correctly pluralized on both wires, but request and response types are code-first. The page-size default and maximum are fixed framework values. |
| [AIP-158](https://google.aip.dev/158) | Enforced | `PageToken` is data-protected before it is serialized, tracks all non-page-size request parameters, permits a new page size, rejects changed filter scope, and is not used for authorization. `ListResultBase` omits the next token at the end. | `skip` is accepted as a framework extension and a negative effective value is clamped to zero. Applications that publish another collection endpoint must implement equivalent paging there. |
| [AIP-160](https://google.aip.dev/160) | Supported by extension point | `UseAip()` registers the AIP expression compiler; `ListAsync` parses it, pushes supported terms to a repository query, evaluates a residual expression when configured, and maps expression failures to validation errors. `UseCel()` exposes a separate CEL language. | Filtering is opt-in and an application chooses enabled languages, strict or residual mode, supported fields, functions, and documented restrictions. |
| [AIP-159](https://google.aip.dev/159) | Partial | `ReadAcrossAttribute` enables `-` for the List parent and `ResourceIdentifiers.ApplyParent` rejects it without that opt-in. Canonical response names come from entity-to-summary mapping. | Get does not implement cross-collection lookup. Cross-parent `order_by` is not rejected or documented as best effort, and resource List responses do not expose AIP-217 unreachable parents. |

Schemata's registered resource path requires an addressable canonical-name pattern whose final collection segment is followed by a placeholder. An AIP-156 singleton instead ends in a static segment and has no resource ID, so it cannot use the standard generated resource surface.

| AIP | Status | Framework boundary | Application responsibility |
| --- | --- | --- | --- |
| [AIP-156](https://google.aip.dev/156) | Not implemented | `ResourceNameDescriptor` treats a terminal pattern without a leaf placeholder as non-addressable, and `AddResource` rejects it before registering CRUD handlers or transports. | Expose a separate application endpoint or extend the resource system with singleton-specific registration, lifecycle, Get/Update, and optional AIP-159 List behavior. An `Operations` whitelist on an ordinary ID-bearing resource does not make it a singleton. |

### Create, Update, and Delete

[AIP-133](https://google.aip.dev/133), [AIP-134](https://google.aip.dev/134), and [AIP-135](https://google.aip.dev/135) prescribe canonical protobuf request messages. Schemata instead exposes the code-first request shapes in the wire-contract table.

| AIP | Status | Implemented behavior | Gap or application responsibility |
| --- | --- | --- | --- |
| [AIP-133](https://google.aip.dev/133) | Partial | HTTP Create uses `POST`, returns `201`, sets `Location`, and returns the created detail; gRPC returns the detail after the same create pipeline. Sanitize wraps remove server system fields before mapping; `AdviceAddCanonicalName` resolves the canonical name from the pattern on add and `AdviceAddUniqueness` rejects duplicates with `ALREADY_EXISTS`. | A first-class AIP-133 user-specified ID field, a distinct resource body field, and required-field semantics are application DTO and validation responsibilities. Standard create is synchronous, not `google.longrunning.Operation`. |
| [AIP-134](https://google.aip.dev/134) | Partial | HTTP uses `PATCH`; `IUpdateMask` accepts `*` and comma-separated wire paths, resolves aliases such as `name` and `etag`, and removes system-field paths. `IAllowMissing` creates when the target is absent. Parent fields are cleared before mapping, so a body cannot reparent the resource. | The field mask is a string instead of `google.protobuf.FieldMask`; an omitted mask maps the full request rather than being represented as an AIP request wrapper. State mutability must be protected by application mapping or validation. Standard update is synchronous. |
| [AIP-135](https://google.aip.dev/135) | Partial | HTTP uses `DELETE` with no body. `allow_missing` returns an empty successful result for an absent resource. Soft delete returns the detail and hard delete returns empty. | Schemata performs no built-in check for existing child resources. It therefore does not automatically return `FAILED_PRECONDITION`, exempt singleton children, or implement a `force`-controlled cascading delete. Applications must enforce child ownership and cascade rules in an advisor or handler. |
| [AIP-154](https://google.aip.dev/154) | Supported by extension point | When a request implements `IFreshness`, HTTP receives `etag` from the body, query, or `If-Match`; gRPC receives the protobuf `etag`. Freshness advisors compare it with an `IConcurrency` timestamp-derived weak ETag and reject a mismatch with `ABORTED`. Omitted or whitespace tags opt out. | An application must put `IFreshness` on the relevant request and `IConcurrency` on the entity, decide whether freshness is required, and document its weak ETag behavior. |
| [AIP-155](https://google.aip.dev/155) | Partial | A request implementing `IRequestIdentification` with a nonempty `RequestId` is cached by entity type, operation, caller, canonical target, request ID, and payload hash. An exact completed replay returns its cached response; an unresolved competing reservation raises `ABORTED`. Create, Update, and qualifying custom-method registration install the advisors. | Because the payload hash is part of the cache key, reuse of one request ID with a changed payload does not collide with the original entry. Requests without `IRequestIdentification`, an empty value, a suppressed advisor, or an operation without an idempotency token do not use the mechanism. Request-ID format, retention, and any stricter uniqueness policy are application responsibilities. |
| [AIP-161](https://google.aip.dev/161) | Partial | Update masks resolve wire names to CLR names and omit system fields from mapping. Wildcard `*` maps the full request. | The surface has no `google.protobuf.FieldMask` type and no read-mask implementation. Map-key, repeated-field, and read/write consistency requirements are not a general framework contract. |
| [AIP-163](https://google.aip.dev/163) | Supported by extension point | `IValidation.ValidateOnly` causes create and update validation advisors to run and then short-circuit before mapping or persistence. | Applications must implement validation and authorization advisors for their actual constraints, and must add `IValidation` to each mutating request that offers the option. The response is a no-content path, not a promise to reproduce every live response header and body. |

## Lifecycle and collection variants

| AIP | Status | Implemented behavior | Gap or application responsibility |
| --- | --- | --- | --- |
| [AIP-157](https://google.aip.dev/157) | Not implemented | Resources register a static `TDetail` for Get/Create/Update and `TSummary` for List. | Static detail/summary selection is not a field-mask parameter, `read_mask`, or request `view` enum. Schemata provides no per-request partial-response mechanism. |
| [AIP-164](https://google.aip.dev/164) | Partial | `ISoftDelete` changes repository removal into a timestamped soft delete. List hides deleted rows unless `show_deleted`; Update rejects deleted rows; built-in `undelete` and `expunge` methods are registered for soft-delete resources. Undelete clears delete and purge times. Expunge physically removes a deleted resource. | The generic framework does not prove the application has a separate expunge permission, a `DELETED` state value, or a documented purge period. Wire request shapes remain code-first rather than canonical AIP requests. |
| [AIP-165](https://google.aip.dev/165) | Partial | `PurgeHandler` and `PurgeJob<TEntity>` implement a collection-scoped `:purge` action. `PurgeFilter` accepts AIP filter semantics and `*`; `force=false` returns a count and sample without deletion; `force=true` deletes and omits the sample. The sample cap is 100 and job arguments persist for scheduler restart recovery. | The operation is Schemata `Operation`, not automatically `google.longrunning.Operation`. Its JSON output is structured while gRPC output is a string, so it cannot claim the AIP long-running wire contract. |
| [AIP-214](https://google.aip.dev/214) | Partial | `IExpiration.ExpireTime` supplies an absolute `DateTime?` expiration property that transports project as `expire_time`. | There is no `expiration` oneof, `Duration` `ttl`, input-only TTL, or required return-time conversion from TTL to `expire_time`. |
| [AIP-217](https://google.aip.dev/217) | Not applicable | The resource List response has no `unreachable` field, `return_partial_success` request field, or partial-success option. | Insight has a separate `QueryInsightResponse.Unreachable` list populated during multi-source execution and mapped by the Insight gRPC adapter. That Insight-only surface does not implement AIP-217 for resources, does not make resource List partial-success capable, and must be reviewed separately for the full AIP contract. |

## Draft and unavailable designs

| AIP | Published status | Schemata status | Checked runtime surfaces | Missing capability |
| --- | --- | --- | --- | --- |
| [AIP-162: Resource revisions](https://google.aip.dev/162) | Draft | Not implemented | Resource registry, CRUD and custom handlers, HTTP conventions, gRPC method registration, protobuf model configuration, and descriptor bridge expose no revision type or route. | Revision resources, snapshots, aliases, rollback, revision ordering, and revision standard methods. |
| [AIP-231: Batch Get](https://google.aip.dev/231) | No maturity banner on the published page | Not implemented | Standard gRPC registration contains List/Get/Create/Update/Delete only; HTTP CRUD conventions expose the same operations; custom methods are unary declared verbs. | Batch request/response types, atomic batch handler, `:batchGet` route, and matching descriptor registration. |
| [AIP-233: Batch Create](https://google.aip.dev/233) | No maturity banner on the published page | Not implemented | No batch request DTO, handler, HTTP route, gRPC RPC, or protobuf registration exists. | Atomic or long-running partial-success batch creation and `failed_requests` metadata. |
| [AIP-234: Batch Update](https://google.aip.dev/234) | No maturity banner on the published page | Not implemented | No batch request DTO, handler, HTTP route, gRPC RPC, or protobuf registration exists. | Atomic or long-running partial-success batch update and hoisted update-mask semantics. |
| [AIP-235: Batch Delete](https://google.aip.dev/235) | No maturity banner on the published page | Not implemented | No batch request DTO, handler, HTTP route, gRPC RPC, or protobuf registration exists. | Atomic or long-running batch delete, `:batchDelete`, and per-request failure metadata. |

A custom verb named `batchCreate`, `batchUpdate`, or `batchDelete` does not implement a batch AIP. It is an AIP-136 unary custom method unless an application supplies every required batch request, handler, atomicity or long-running behavior, transport route, response, and failure contract.

## Source map

| Concern | Executable source |
| --- | --- |
| Standard CRUD pipeline | `src/Schemata.Resource.Foundation/ResourceOperationHandler.Create.cs`, `.Get.cs`, `.List.cs`, `.Update.cs`, `.Delete.cs` |
| Custom-method pipeline | `src/Schemata.Resource.Foundation/ResourceMethodOperationHandler.cs`, `src/Schemata.Abstractions/Resource/ResourceMethodAttribute.cs` |
| HTTP endpoints and custom routes | `src/Schemata.Resource.Http/ResourceController.cs`, `ResourceControllerConvention.cs`, `ResourceMethodController.cs`, `ResourceMethodControllerConvention.cs` |
| gRPC registration | `src/Schemata.Resource.Grpc/ResourceService.cs`, `ResourceServiceMethodProvider.cs`, `ResourceCustomMethod.cs`, `FileDescriptorBridge.cs` |
| Trait-aware HTTP and gRPC fields | `src/Schemata.Transport.Http/SchemataJsonTraits.cs`, `src/Schemata.Transport.Grpc/Proto/SchemataProtoModelConfigurator.cs`, `src/Schemata.Common/ResourceWireNameRules.cs` |
| Pagination, filters, masks, validation, freshness, and idempotency | `src/Schemata.Resource.Foundation/ResourceOperationHandler.List.cs`, `.Update.cs`, `Advisors/ValidationHelper.cs`, `Advisors/ResourceDetailResponsePipelineAdvisor.cs`, `Advisors/ResourceIdempotencyPipelineAdvisor.cs` |
| Soft delete and purge | `src/Schemata.Entity.Repository/Advisors/AdviceRemoveSoftDelete.cs`, `src/Schemata.Resource.Foundation/UndeleteHandler.cs`, `ExpungeHandler.cs`, `PurgeHandler.cs`, `PurgeJob.cs` |
| Insight-only unreachable resources | `src/Schemata.Insight.Skeleton/Wire/QueryInsightResponse.cs`, `src/Schemata.Insight.Foundation/Execution/PlanExecutor.cs`, `src/Schemata.Insight.Grpc/Mapping/InsightStructMapper.cs` |
