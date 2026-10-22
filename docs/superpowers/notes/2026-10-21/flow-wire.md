# Flow wire-request / dispatch-command unification (#368, plan Task 14) — blocked on scope

Status: **analysis complete, no source edits made, blocked for scope extension.** Per the Lead's
instruction ("If a clean single-envelope direction is not feasible without another component's file,
stop and report"), I stopped before touching out-of-scope code. This file records the design choice,
the blocker with exact files/lines, and the concrete implementation plan that needs approval.

## 1. Current state verified (HEAD `cb7b598e`)

Per operation (`complete`, `correlate`, `signal`) there are two request types and two envelope paths:

| Operation | Wire request (`Flow.Skeleton.Models`) | Dispatch command (`Flow.Foundation.Commands`) |
|---|---|---|
| complete | `CompleteActivityRequest` (`ICommand<ProcessSnapshot>`, `ICanonicalName`, `IRequestPrincipal`; body `Token`) | `CompleteActivityRequest(string ProcessCanonicalName, string? Token, ClaimsPrincipal? Principal)` (`IProcessScoped`) |
| correlate | `CorrelateMessageRequest` (body `MessageName`, `Payload` string, `Token`) | `CorrelateMessageRequest(string ProcessCanonicalName, string MessageName, object? Payload, string? Token, ClaimsPrincipal? Principal)` |
| signal | `ThrowSignalRequest` (body `SignalName`, `Payload` string, `Token`) plus `IRequestIdentification` / `IResourceDispatchCarrier` | `ThrowSignalRequest(string SignalName, object? Payload, string? Token, ClaimsPrincipal? Principal)` |

Paths:
1. **Transport**: `ResourceMethodOperationHandler.InvokeAsync` → `ResourceMethodRequest<TProcess, SkeletonModel, R>`
   → `ResourceMethodDispatchHandler` (resource pipeline; registered by `Resource.Foundation` for the
   handler descriptor in `FlowResourceRegistration.ProcessMethods`) → bare wire model → wire handler
   (`CompleteActivityHandler` / `CorrelateMessageHandler` / `ThrowSignalHandler`) → **second dispatch**
   of the internal command → keyed default handler.
2. **`FlowRunner`**: `ResourceMethodRequest<TProcess, InternalCommand, R>` → `ResourceMethodForwardHandler`
   (registered at `Flow.Foundation/Extensions/ServiceCollectionExtensions.cs:205-213`) → internal command
   → default handler.

Security wraps are registered for both envelope shapes: `Resource.Foundation/ResourceAuthorizationRegistration.cs`
(transport / wire-model envelope, via `SchemataFlowBuilder`'s resource-builder `WithAuthorization`) and
`FlowAuthorizationRegistration.cs` (runner / command envelope). That is the X-18 duplicate: one operation,
two envelope types, one security semantic registered twice.

## 2. Candidate directions and why each breaks the assigned scope

The assignment scope is `src/Schemata.Flow.Skeleton/**` + `src/Schemata.Flow.Foundation/**` +
three Flow test projects. Flow.Actor, Flow.Event, Flow.Http, Flow.Grpc, Flow.Scheduling,
Resource.Foundation and Messaging.Skeleton are out of scope.

### Direction 1 — one wire-model type per operation (the plan's "Report `GenerateReportRequest` pattern")

Make `Flow.Skeleton.Models.{CompleteActivityRequest, CorrelateMessageRequest, ThrowSignalRequest}` the
single dispatch command, delete the three `Flow.Foundation/Commands` duplicates and the wire→command
forwarding handlers. **Blockers (all out-of-scope files):**

- `src/Schemata.Flow.Actor/Features/SchemataFlowActorFeature.cs:19-20` aliases
  `Schemata.Flow.Foundation.Commands.{CompleteActivityRequest,CorrelateMessageRequest}` and
  `:54-55` registers actor serialization for them; the registration constraint at `:66` is
  `IRequest<TResult>, IProcessScoped`. Deleting the command types breaks the build, and the unified
  Skeleton types cannot implement `IProcessScoped` as long as the interface lives in
  `Flow.Foundation` (Skeleton must not reference Foundation) — the interface has to move to Skeleton and
  `src/Schemata.Flow.Actor/Handlers/FlowActorRouting.cs:1-14` (one `using`/`ProcessCanonicalName` read)
  must follow.
- `src/Schemata.Flow.Event/Handlers/FlowEventHandler.cs:12,51-53` constructs
  `Foundation.Commands.CorrelateMessageRequest` positionally with a **typed** payload
  (`new(sub.Target, sub.EventType, payload, sub.Token, Principal: null)`); the alias must point at the
  unified Skeleton type (one line, but it is in Flow.Event, not in scope).
- `Schemata.Flow.Actor.Tests` (in scope) would also need its command-type aliases updated.

So Direction 1 is feasible only with a small but real cross-component edit in **Flow.Actor (2 files) and
Flow.Event (1 file)**.

### Direction 2 — keep internal commands; route `FlowRunner` through the wire-model envelope

Keep `Flow.Foundation.Commands.*`; change `FlowRunner` to dispatch
`ResourceMethodRequest<TProcess, SkeletonModel, R>` so both entries share one envelope type, then delete
the internal-command registrations (`ServiceCollectionExtensions.cs:206-208`) and their
`FlowAuthorizationRegistration` entries. **Blocker (this component / a shared one, not mine):**

- The wire-model envelope has exactly one handler in the product: `ResourceMethodDispatchHandler`,
  which is only registered when the Flow resource is installed by the HTTP/gRPC features
  (`Resource.Foundation/Extensions/ServiceCollectionExtensions.cs:249-300` runs from
  `ResourceRegistry` installation). `FlowRunner` is transport-independent by design and is used in
  hosts/tests with only `AddSchemataFlow` (e.g. `Flow.Tests`, `Flow.Actor.Tests`,
  `Flow.Integration.Tests/EventCompositionShould`) — the envelope would have **no handler** there.
  Registering a fallback `ResourceMethodForwardHandler` for the wire envelope in `AddFlowExecution`
  is not clean: `AddFlowExecution` runs before the resource registry installs, and both use
  first-wins `TryAdd`, so the transport path would silently lose the resource pipeline (method
  idempotency via `IResourceDispatchCarrier`, `IResourceMethodRequestAdvisor` / `IResourceMethodAdvisor`,
  instance access stages) — a behavior regression this task must not introduce.
- It also does not remove the duplicated security semantics: `FlowRunner` would need wraps on the bare
  wire request (new registration) while the resource pipeline keeps its envelope wraps, i.e. security
  would run twice on the transport path instead of once.

### Direction 2b — keep both envelope paths but delete the wire→command forwarding layer

Not a unification: two request types and two envelope paths remain; fails the acceptance
("每个 Flow 操作只剩一个公开 request 类型与一条 envelope 路径").

## 3. Recommended implementation (needs Lead approval + scope extension)

Direction 1 (the plan's stated direction), with these additional write scopes:

- `src/Schemata.Flow.Actor/Features/SchemataFlowActorFeature.cs` — repoint the two type aliases and the
  two `Register<,>` calls at the unified `Schemata.Flow.Skeleton.Models` types (mechanical; the actor
  bridge keeps wrapping the same logical command).
- `src/Schemata.Flow.Actor/Handlers/FlowActorRouting.cs` — resolve the routing key from the unified
  request type (`CanonicalName`, or `IProcessScoped` moved to Skeleton).
- `src/Schemata.Flow.Event/Handlers/FlowEventHandler.cs` — repoint the one alias; the unified
  `CorrelateMessageRequest` keeps the 5-argument construction (`ProcessCanonicalName`, `MessageName`,
  typed payload, `Token`, `Principal`) so the event bridge behavior is unchanged; the wire-facing
  `Payload` string property stays as the JSON payload surface and a `[JsonIgnore]` typed payload slot
  carries the bridge's already-materialized event object (same split as `FlowPayload.Typed` today).

Then in scope:

1. `Flow.Skeleton/Models/{CompleteActivityRequest,CorrelateMessageRequest,ThrowSignalRequest}` — absorb
   the command payload (process identity via `CanonicalName`, actor routing via a Skeleton-owned
   `IProcessScoped`, typed-payload slot); move `IProcessScoped` from Foundation to Skeleton.
2. Delete `Flow.Foundation/Commands/{CompleteActivityRequest,CorrelateMessageRequest,ThrowSignalRequest}`;
   retarget `Default{Complete,Correlate,ThrowSignal}Handler` at the unified types; delete the
   `CompleteActivityHandler` / `CorrelateMessageHandler` / `ThrowSignalHandler` forwarding layer and give
   `FlowResourceRegistration.ProcessMethods` a single non-generic adapter per operation that invokes the
   keyed default handler directly (one dispatch, no second request type).
3. `FlowRunner` dispatches the unified request directly; remove the three `ResourceMethodForwardHandler`
   registrations and the three command-envelope wrap pairs; the remaining wrap registration lives on the
   unified wire-model envelope (registered by the resource builder once).
4. Tests: extend `FlowEntryEquivalenceShould` (facade and dispatcher hit the same handler/one request type),
   rewrite `FlowMethodEnvelopeShould` (one envelope shape, no bare command hop), assert in
   `FlowAuthorizationRegistrationShould` that each Flow operation has exactly one auth/authz wrap pair;
   update `Flow.Tests`/`Flow.Actor.Tests` command-type aliases and the three integration test files that
   reference `Skeleton.Models` types.
5. Run `bash /tmp/dsh-build.sh dotnet build` + the three test projects for net8.0/net10.0.

Risk that needs a decision: removing the bare wires from `Flow.Foundation/Commands` is a breaking public
API move for anyone using those command types directly (they are public today, and the bridges are
in-repo proof of consumers). Moving `IProcessScoped` to `Skeleton` is likewise a public API relocation.

## 4. What I did not do

- No source, test or docs file was modified; no build/test was run (nothing to verify).
- Did not edit Flow.Actor/Flow.Event or any other out-of-scope component, and did not invent a
  fallback implementation path inside Flow.Foundation that would diverge from the plan.

## 5. Decision requested

Approve Direction 1 with the three extra bridge files above added to my write scope (they are mechanical
alias/using changes; behavior of the actor/event bridges is preserved), or direct a different trade-off
(e.g. accept Direction 2 with the documented transport-pipeline regression, or defer #368).
