# Business Logic and Google AIP

This reference is for Schemata maintainers and application developers who place resource actions, state changes, asynchronous work, authorization, and errors in the correct runtime component. It covers [AIP-151](https://google.aip.dev/151), [AIP-152](https://google.aip.dev/152), [AIP-153](https://google.aip.dev/153), [AIP-193](https://google.aip.dev/193), and [AIP-211](https://google.aip.dev/211). Resource and transport mechanics are defined in [AIP interactions](aip-interactions.md); resource shape and field ownership are defined in [AIP modeling](aip-modeling.md); the shared error types and full envelope are defined in [Error Model](../core/error-model.md).

## Canonical implementation rule

**Implement one business action in one owner.** A transport adapter, resource advisor, handler, Flow, and scheduled job may call or surround that owner, but they must not each reimplement the action's state checks, side effects, or persistence rule. Put cross-cutting policy at the boundary and keep the action's state transition in its selected owner.

## Selecting the owner

| Need | Owner | Runtime responsibility | Status |
| --- | --- | --- | --- |
| Persistent field semantics and metadata | Entity trait or attribute | Declares data shape and discoverable resource metadata. It does not execute an action. | Supported by extension point |
| Invariant that must hold for every persistence path | Repository advisor | Applies the invariant at repository work and participates in that persistence boundary. | Supported by extension point |
| Envelope-wide request policy | Request pipeline advisor | The dispatcher runs `IRequestPipelineAdvisor<TRequest,TResponse>` around the complete request and response. Authentication, coarse authorization, sanitization, validation, idempotency, and response shaping use this position. | Supported by extension point |
| Rule requiring a query container or loaded entity | Resource handler advisor | Entitlement, instance authorization, freshness validation, and entity policy run inside handler stages. | Supported by extension point |
| One resource action, state transition, or immediate side effect | Custom-method handler | Owns the business verb and its result. The method envelope carries the verb through dispatcher policy. | Supported by extension point |
| Durable multi-step process, waits, compensation, or external events | Flow | Persists process, token, transition, source, and compensation state together; bridges may resume it from events or timers. | Supported by extension point |
| Background, delayed, periodic, restart-recoverable work | Scheduling job | A durable execution row is created before the job body; the dispatcher claims and runs it. | Supported by extension point |
| Route, caller identity, serializer, RPC descriptor, and error envelope | HTTP or gRPC transport | Adapts a completed request to and from the wire. It does not own domain invariants. | Enforced |

### Business rules versus wire adaptation

Resource envelopes run dispatcher wraps before handler work and unwind their after segments after the handler returns. Handler stages apply container-scoped and entity-dependent policy. `ResourceMethodOperationHandler` creates the method envelope and delegates to the dispatcher; its Resource dispatch handler loads an instance target when needed and invokes the inner custom-method handler. A custom-method handler is therefore the natural owner for an action such as `publish`.

`SchemataJsonTraits` and `SchemataProtoModelConfigurator` project resource traits to HTTP JSON and gRPC protobuf. They share wire-name rules for `name`, `etag`, and list-resource plurals. HTTP applies snake_case, kebab-case enums, string-valued `long`, and null omission. gRPC creates protobuf-net runtime-model members and descriptors. These transformations are transport adaptation, not business behavior. See [AIP interactions](aip-interactions.md) for the complete request-to-wire matrix.

## State changes, side effects, and transactions

A standard Update runs its sanitize, validation, and idempotency wraps, then maps a request into an existing entity inside the verb handler after the update entity advisors (soft-deleted rejection, freshness, child-parent) run, and commits the repository (`src/Schemata.Resource.Foundation/ResourceOperationHandler.Update.cs`). Use it for a resource representation change. Use a custom method when the action has a distinct business verb, precondition, state transition, or side effect. Do not disguise that action as an unrelated field update.

A create handler follows the equivalent wrap, mapping, entity-advisor, repository-write, commit, and response-wrap sequence (`src/Schemata.Resource.Foundation/ResourceOperationHandler.Create.cs`). An invariant that must survive all write paths belongs in a repository advisor or domain persistence mechanism, rather than being copied into each HTTP or gRPC handler.

Flow is the owner when the process itself needs durable state across steps. `ProcessPersistence.ExecuteAsync` joins process, token, transition, source, and compensation repositories to one unit of work, commits after its callback, and rolls back if the callback throws (`src/Schemata.Flow.Foundation/ProcessPersistence.cs`). `FlowHandlerSupport` runs source and transition advisors and arms registered catch handlers in that persistence work (`src/Schemata.Flow.Foundation/Handlers/FlowHandlerSupport.cs`). External event and timer bridges should resume the Flow; they should not duplicate the process transition.

Scheduling is the owner when an action must run later, repeatedly, or recover after restart. `DefaultScheduler.TriggerAsync` persists a pending execution before returning; `JobExecutionDispatcher` claims due rows through a `Pending` to `Running` update guarded by the execution concurrency token, executes advisors, observers, and the job body, then records its result (`src/Schemata.Scheduling.Foundation/JobExecutionDispatcher.cs`). Scheduling does not join its trigger write to a caller's business transaction; `TriggerAsync` gives that boundary eventual consistency.

## AIP-151: Long-running operations

AIP-151 requires a potentially long operation to return `google.longrunning.Operation`, use `google.longrunning.operation_info` with response and metadata types, and expose the Operations service. Start failures use the ordinary AIP-193 error response; execution failures are reported in the operation's `error` as `google.rpc.Status`.

### Schemata mapping

| Dimension | Observed contract | AIP-151 status and gap |
| --- | --- | --- |
| Internal CLR shape | `Operation` carries `Done`, `Error`, `Response`, `Metadata`, and canonical name. `OperationStatus` has only `int Code` and `string Message`; `OperationResponse` stores serialized output. | Partial. The shape has familiar fields, but it is a Schemata type rather than `google.longrunning.Operation`. |
| Scheduling transformation | `OperationMapper.FromExecution` maps a `SchemataJobExecution` row to `operations/{uid}`. Only `Succeeded`, `Failed`, and `Cancelled` are terminal; failed executions map to code `2` (`UNKNOWN`) and cancelled executions to `1` (`CANCELLED`). | Partial. Failure mapping loses typed details and maps every failed job to `UNKNOWN`. `Blocked` and `Skipped` remain `done=false`. |
| HTTP JSON | HTTP writes the response output as structured JSON through `RawJsonConverter`; trait projection exposes canonical name as `name`. Scheduling registers the execution resource with Get, List, Delete, `:cancel`, and `:wait`. | Partial. The envelope is Schemata's resource representation, not the required `google.longrunning.Operation` message or Operations-service HTTP surface. |
| gRPC protobuf | gRPC uses the registered execution resource and protobuf-net runtime model. `OperationResponse.Output` remains a string in the protobuf model. | Partial. It does not return the `google.longrunning.Operation` protobuf type, use `operation_info`, or implement `google.longrunning.Operations`. |
| Registration boundary | `AddSchemataScheduling` registers `IOperationService`; Scheduling HTTP and gRPC features register `SchemataJobExecution` as the `Operation` resource. | Supported by extension point. An application must register Scheduling and expose its selected transport. |

`RunJobHandler` loads a persisted job, calls `IScheduler.TriggerAsync`, and returns the mapped operation only after the pending execution row exists (`src/Schemata.Scheduling.Foundation/RunJobHandler.cs`). `IOperationService.GetAsync`, `WaitAsync`, and `CancelAsync` read and change that durable row (`src/Schemata.Scheduling.Foundation/DefaultOperationService.cs`). `:wait` bounds server-side polling to 30 seconds and returns the current snapshot on timeout (`src/Schemata.Scheduling.Foundation/WaitOperationHandler.cs`).

The operation envelope differs materially from `google.longrunning.Operation`. `OperationStatus` has no `details` collection, so it cannot carry a `google.rpc.Status` detail payload. Schemata has no `google.longrunning.Operations` service, no `GetOperation` or `WaitOperation` RPC on that service, and no `google.longrunning.operation_info` annotation. The execution resource's Get/List/Delete and `:cancel`/`:wait` methods are Schemata resource operations, not replacements for those protobuf contracts.

## AIP-152: Jobs

AIP-152 defines a `*Job` resource configured by standard methods and a `Run*` custom method that starts it, uses HTTP `POST` with a `:run` suffix, and normally returns an AIP-151 long-running operation whose result reports the run. It also defines request naming, resource-reference annotation, and optional execution subresources.

Schemata Scheduling has a `SchemataJob` resource and registers the `:run` method (`src/Schemata.Scheduling.Foundation/SchedulingResourceRegistration.cs`). The handler creates an addressable execution row and returns Schemata's `Operation` envelope. The framework also represents execution rows as a separately readable, listable, deletable resource. HTTP and gRPC feature registrations expose these resources only when the corresponding Scheduling transport feature is enabled (`src/Schemata.Scheduling.Http/Features/SchemataSchedulingHttpFeature.cs`, `src/Schemata.Scheduling.Grpc/Features/SchemataSchedulingGrpcFeature.cs`).

**Status: Partial.** The job/execution split and `:run` background dispatch are available, but the returned envelope has the AIP-151 gaps above. The implementation does not establish the AIP's required protobuf request naming, `google.api.resource_reference`, `google.api.http` annotation, or `Run*Response` result-message contract. An application remains responsible for a public Job resource's names, permissions, result schema, and job-body idempotency.

## AIP-153: Import and export

AIP-153 specifies import and export custom methods, request source or destination `oneof` shapes, HTTP POST contracts, and usually an AIP-151 operation. It places partial failures in LRO metadata as `google.rpc.Status` values.

**Status: Not implemented.** Schemata has no import/export resource methods, request model, source/destination `oneof` model, or Flow/Scheduling specialization for these operations. A generic custom method or scheduled job is not an AIP-153 implementation. Do not claim AIP-153 support until a complete public contract and its HTTP and gRPC registrations implement these requirements.

## AIP-193: Errors

AIP-193 requires API errors to use `google.rpc.Status` and canonical `google.rpc.Code`; it requires `ErrorInfo` in details, with stable `(reason, domain)`, constraints on reason and metadata keys, and rules for localized and authorization errors. [`google/rpc/code.proto`](https://github.com/googleapis/googleapis/blob/master/google/rpc/code.proto) supplies canonical numeric values and code semantics. [`google/rpc/status.proto`](https://github.com/googleapis/googleapis/blob/master/google/rpc/status.proto) defines `Status.code`, `Status.message`, and repeated `Status.details`. Those proto definitions do not themselves enforce the AIP-193 `ErrorInfo`, stability, wording, localization, or authorization rules.

**Status: Partial.** `SchemataException.CreateErrorResponse` creates an `ErrorResponse` with `ErrorInfo` when absent, adds request information when present, and may add a localized message (`src/Schemata.Abstractions/Exceptions/SchemataException.cs`). The HTTP exception handler serializes that response with the exception's HTTP code (`src/Schemata.Transport.Http/Extensions/ApplicationBuilderExtensions.cs`). Its `error.code` is the HTTP status, while `error.status` is a canonical-code string. This is the AIP-193 HTTP-style core information, rather than a JSON serialization of `google.rpc.Status` whose `code` is the canonical numeric code.

The gRPC interceptor builds `Google.Rpc.Status`, packs recognized typed details into `google.protobuf.Any`, and places it in `grpc-status-details-bin` (`src/Schemata.Transport.Grpc/Proto/RpcStatusBuilder.cs`). Its canonical mapping recognizes `OK`, `INVALID_ARGUMENT`, `NOT_FOUND`, `PERMISSION_DENIED`, `ABORTED`, `ALREADY_EXISTS`, `FAILED_PRECONDITION`, `UNAUTHENTICATED`, and `RESOURCE_EXHAUSTED`; other status strings map to `INTERNAL`. This leaves an application responsible for selecting every required canonical code and preserving its intended semantics.

The framework does not validate `ErrorInfo.reason` length or UPPER_SNAKE_CASE format, `ErrorInfo.domain` global uniqueness, metadata-key syntax or length, unique detail types, stable `(reason, domain)` pairs, or metadata-key evolution. `ErrorInfoDetail.Domain` is nullable (`src/Schemata.Abstractions/Errors/ErrorInfoDetail.cs`), and the fallback created by `SchemataException` receives no domain unless a caller supplies one. Localized messages depend on a requested locale and a resolvable resource template; a missing or malformed template is silently omitted. These are AIP-193 gaps, not merely wire differences. See [Error Model](../core/error-model.md) for the exception classes and registered detail types.

LRO execution errors have a narrower contract than normal transport errors: `OperationStatus` carries only code and message. It cannot convey `ErrorInfo`, field violations, localization, help links, retry information, or another `google.rpc.Status.details` payload.

## AIP-211: Authorization checks

The shared `WithAuthentication()` and `WithAuthorization()` extensions configure separate advisor registrations. Authentication checks a non-anonymous principal at the dispatcher boundary. Coarse authorization resolves and matches the operation permission there, applying the AIP-211 existence probe for Get, Update, and Delete. Handler-stage access providers receive the mapped entity on Create and the loaded entity for the other instance operations; entitlement expressions modify query containers.

**Status: Supported by extension point.** `IPermissionResolver` and `IPermissionMatcher` customize coarse authorization. `IAccessProvider<TEntity,TRequest>` customizes instance authorization. `IEntitlementProvider<TEntity,TRequest>` customizes row filtering. Calling `UseSecurity()` alone registers defaults; the matching builder extension registers the domain advisor closures.

## Publication checklist

Before publishing an action:

1. Select exactly one business owner from the decision matrix.
2. Put reusable request policy in advisors and transport adaptation in HTTP or gRPC registration, without duplicating the owner's business rule.
3. Use a custom method for a state transition or side effect; use Flow for durable multi-step orchestration; use Scheduling for delayed or restart-recoverable execution.
4. Treat Schemata `Operation` as a partial AIP-151 surface. Verify the actual HTTP JSON and gRPC protobuf envelope before advertising compatibility with Google long-running operations.
5. Activate authorization with `WithAuthorization()`, implement the access policy, and verify denial ordering and existence behavior on the exposed transport.
6. Use the shared error model for ordinary errors, then verify the canonical code, domain, detail payloads, and transport-specific envelope required by the public API.
