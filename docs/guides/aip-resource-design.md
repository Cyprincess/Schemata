# Design a Resource API with Google AIPs

This guide is for an application developer designing a public Schemata resource API for the first time. Follow the steps in order, then use the linked references to assess each AIP requirement and final transport contract.

## 1. Identify the resource nouns

Start with the business objects that clients need to address, read, create, change, or delete independently. Model each independently addressable noun as a resource. Keep a concept as an entity when it exists only to support persistence or another resource's behavior. Model a relationship as a reference or association when it connects independently managed resources rather than becoming a second canonical parent.

Choose a resource hierarchy before choosing tables or DTOs. A resource hierarchy represents client-facing ownership and containment; it is not a mirror of the persistence schema. Give every child one canonical parent and keep parent-child and client-managed reference paths acyclic.

[AIP-121](https://google.aip.dev/121) describes the resource-oriented sequence. The resource modeling reference records the Schemata assessment for resource boundaries, hierarchy, and association patterns: [AIP Modeling](../documents/resource/aip-modeling.md).

## 2. Define identity and hierarchy

Choose the canonical parent first. A child belongs below that parent only when the parent defines its canonical scope and lifetime. Use a separate top-level resource plus a reference when the relationship is cross-cutting or the child must remain independently addressable.

Define one canonical resource-name pattern for every public resource. The pattern supplies collection segments, the leaf identifier, routes, service names, and parent scoping. Schemata requires an addressable `[CanonicalName]` pattern when registering an `ICanonicalName` resource. Registration is explicit: adding `[Resource]` metadata alone creates neither endpoints nor resource-name resolution. Register every public resource through `AddResource<T>()` or `Use<...>()` while configuring services.

Use a resource reference for an independent resource's canonical name. Decide separately whether the application must verify that the referenced row exists. See [Resource Naming](../documents/resource/resource-naming.md) and the canonical-name, parent, and reference requirements in [AIP Modeling](../documents/resource/aip-modeling.md).

## 3. Separate the API shapes

Define four shapes deliberately:

- **Entity**: persistent data and repository traits.
- **Write request**: client input accepted by Create and Update.
- **Detail response**: representation returned by Get, Create, and Update.
- **Summary response**: representation returned for each List item.

Separate shapes allow a write surface to exclude server-owned or sensitive data and let a list return the fields appropriate for a collection scan. Static `TDetail` and `TSummary` selection is a mapping choice. It is not AIP-157 per-request partial response.

Treat every mapper as part of the public-contract review. Request-to-entity mapping governs which input reaches persistence; entity-to-detail and entity-to-summary mapping governs which values clients receive. Mapping can add, omit, transform, or overwrite fields, so a CLR property alone is insufficient evidence for an HTTP or gRPC field.

For field ownership, standard fields, output-only behavior, and the framework's requirement-level status, use [AIP Modeling](../documents/resource/aip-modeling.md).

## 4. Classify each field

For every field, record its purpose, source of truth, and allowed directions:

1. Mark whether the client owns the value, the server owns it, or the value is computed from other state.
2. Decide whether the field appears on writes, detail responses, summaries, or more than one shape.
3. Mark input-only values, output-only values, immutable values, sensitive values, and values that require field-level validation.
4. Choose the field type, cardinality, unset behavior, and format before publishing a route.
5. Decide how a client supplies references, parent scope, freshness information, pagination, filters, or update masks.

Traits can add persistence behavior, but several traits are data contracts whose values application code must maintain. `IStateful.State` is a string-shaped application model and is not by itself an AIP-216 output-only state enum. `IExpiration.ExpireTime` is an absolute timestamp field and is not by itself an AIP-214 `expire_time`/`ttl` oneof. Review the final resource model against the field and lifecycle entries in [AIP Modeling](../documents/resource/aip-modeling.md).

## 5. Choose the method before the handler

Choose a standard method whenever the requested behavior has its standard semantics:

1. **Get** reads one named resource.
2. **List** reads a collection under its canonical parent or an explicitly supported wider scope.
3. **Create** adds a resource.
4. **Update** changes client-settable fields, with the required update policy and mask behavior.
5. **Delete** removes or soft-deletes a resource according to the resource lifecycle.

Use a custom method when the action is a distinct business verb, state transition, or side effect that Update would obscure. Choose instance scope for an action on one resource and collection scope for an action on a collection. A custom method has its own request, response, method name, and HTTP/gRPC surface; it is not a shortcut for an unmodeled standard or batch method.

AIP-136 uses `GET` for retrieval and `POST` for mutations, and requires a colon plus the custom verb in the HTTP path. Schemata maps instance and collection custom methods to its resource transports. Confirm the final route and RPC name rather than inferring either from the CLR handler type.

Use [AIP Interactions](../documents/resource/aip-interactions.md) for standard methods, custom-method scope, idempotency, freshness, pagination, filtering, update masks, and transport-specific limits.

## 6. Place each rule once

Put durable data invariants at the entity or repository boundary. Put envelope-wide policy in the dispatcher wrap lanes: authentication, coarse authorization, sanitization, validation, idempotency, and response shaping run in that order around the verb handler. Put query scoping and instance checks in handler stages: entitlement expressions filter the target query before rows load, and access providers check the mapped or loaded entity. Keep a single resource action, transition precondition, and its mutation in one custom-method handler.

Use a Flow process when work has durable state across steps, waits for events or time, needs compensation, or has a process lifecycle. Use a Scheduling Job when work must execute later or in the background, repeat on a schedule, survive restarts, or expose durable execution state. Keep one business implementation entry point; transport bindings, advisors, handlers, Flows, and Jobs must not duplicate the same rule.

The business-logic reference defines the boundary between domain behavior and wire adaptation: [AIP Business Logic](../documents/resource/aip-business-logic.md). [Flow](../documents/flow/overview.md) and [Scheduling](../documents/scheduling/overview.md) describe the process and job execution models.

## 7. Choose the completion model

Return the resource result synchronously when the operation can complete within the request and can truthfully report its final user-settable state. Move work to Scheduling when execution should be durable and independently observable. Move work to Flow when the result depends on a persisted multi-step process.

AIP-151 recommends `google.longrunning.Operation` for operations that may take significant time and requires the shared Operations service, operation metadata, and typed response information. Schemata's `Operation` CLR type requires a separate wire review: its HTTP response output is structured JSON, while its gRPC output remains a string field. Its CLR name alone does not establish compatibility with `google.longrunning.Operation` or the shared `google.longrunning.Operations` service.

Select the mechanism only after checking the AIP-151 gap analysis in [AIP Business Logic](../documents/resource/aip-business-logic.md). Do not label an asynchronous custom method as AIP-151 compliant from its name or return type alone.

## 8. Activate authorization and map errors

Enable the security feature and activate transport security explicitly with `UseResource().WithAuthorization()`, the `IResourceBuilder` extension from `Schemata.Security.Foundation`. `WithAuthentication(scheme)` checks a non-anonymous principal at the dispatcher boundary; `WithAuthorization()` adds the coarse operation check and registers the access and entitlement advisor families. Without that call, the providers stay dormant. Provide permission resolution, access, and entitlement policies appropriate for the resource and operation.

Review authorization against AIP-211 before publishing. It requires authorization before request validation and `PERMISSION_DENIED` with an ambiguous message when authorization fails. For a missing resource, it recommends a parent read check before `NOT_FOUND` when authorization cannot otherwise be determined. Schemata's built-in resource helper has different behavior for a denied Get operation, returning `NOT_FOUND` directly; confirm the implementation and policy ordering for the specific resource rather than relying on an attribute, endpoint filter, or provider registration.

Trace error behavior on each enabled transport. HTTP writes a JSON error envelope through its exception handler. gRPC maps failures to `google.rpc.Status` in `grpc-status-details-bin`. AIP-193 status, detail, localization, and authorization requirements have transport and application-policy gaps recorded in [AIP Business Logic](../documents/resource/aip-business-logic.md).

## 9. Verify the final wire contract

Trace each operation from a real request to its final response on every enabled transport:

1. Start with HTTP model binding or protobuf unmarshalling.
2. Record dispatcher-wrap policy: authentication, coarse authorization, sanitization, validation, and idempotency. Record handler-stage target resolution, entitlement filtering, and instance access.
3. Record request-to-entity and entity-to-response mapping.
4. Record response advisors and result-envelope construction.
5. Inspect the HTTP JSON produced by `SchemataJsonTraits`.
6. Inspect the gRPC protobuf shape produced by `SchemataProtoModelConfigurator` and the resource registration boundary.

The serializers transform the internal shape. Both transports suppress `ICanonicalName.Name`, expose `CanonicalName` as `name`, expose freshness as `etag`, and rename a list result's `Entities` field to the resource plural. HTTP then applies its JSON configuration, including snake_case naming and null handling. Registered gRPC request, detail, summary, list-result, and custom-method types receive protobuf-net model configuration with snake_case field names; its descriptor and registration boundary determine which types appear in reflection.

A resource may be HTTP-only, gRPC-only, or available on both. Verify route generation, protobuf registration, serialization, and error envelopes separately. User serializer configuration and unregistered types can change the observable contract. The reference matrix and transport tracing procedure are in [AIP Interactions](../documents/resource/aip-interactions.md).

## 10. Check unsupported capabilities

Treat a generic extension point as an opportunity to build an application feature, not proof that Schemata implements a specialized AIP. The following capabilities are **Not implemented**:

- AIP-162 resource revisions. This AIP is currently Draft.
- AIP-231 Batch Get.
- AIP-233 Batch Create.
- AIP-234 Batch Update.
- AIP-235 Batch Delete.

A custom method, collection loop, or `Operation` wrapper does not implement the request shapes, atomicity or partial-success semantics, handlers, registrations, routes, serializer support, and descriptors required by these AIPs. Design a distinct application feature only when its complete contract is required, and classify it against the relevant AIP before publishing.

## Final release checklist

### Resource model

- [ ] Every public noun has a justified resource, entity, reference, or association role.
- [ ] Each child has one canonical parent and each public resource has an addressable canonical-name pattern.
- [ ] The entity, write request, detail response, and summary response expose intentional fields.
- [ ] Field ownership, sensitivity, input-only, output-only, lifecycle, reference, and unset behavior are recorded.
- [ ] Every public resource is explicitly registered.

### Methods and business logic

- [ ] Each operation uses a standard method or has a documented reason for a custom method.
- [ ] State transitions and side effects use a handler or process boundary rather than a misleading Update.
- [ ] Invariants, API policy, action logic, and long-running work each have one owner.
- [ ] The synchronous, Job, Flow, or operation choice matches execution duration and durability needs.

### Security and wire contract

- [ ] `UseResource().WithAuthorization()` activates the intended resource security advisors; `WithAuthentication(scheme)` sets the endpoint scheme.
- [ ] Access policy, entitlement filtering, authorization failure behavior, and error mapping are reviewed for each operation.
- [ ] HTTP JSON and gRPC protobuf are exercised separately after advisor and mapper transformations.
- [ ] Resource registration, route/RPC generation, serializer aliases, result envelopes, and error envelopes match the published contract.
- [ ] Unsupported AIPs are absent from the API claim set.

## See also

- [AIP Modeling](../documents/resource/aip-modeling.md)
- [AIP Interactions](../documents/resource/aip-interactions.md)
- [AIP Business Logic](../documents/resource/aip-business-logic.md)
- [Resource Overview](../documents/resource/overview.md)
- [Resource Naming](../documents/resource/resource-naming.md)
- [Security](../documents/security.md)
