# Nullable sweep — group A (#379 / Task 18)

Scope: group A projects under `src/Schemata.{Abstractions,Common,Core,Advice,Modular,Actor.*,Flow.*,
Messaging.*,Event.*,Mapping.*,Expressions.*,Domain.Skeleton,Caching.*,Validation.*,Transport.*}/**`,
with the task-7 exclusions (Core/State, Flow.Repository / Scheduling.Repository dirs,
Flow.Foundation/Repository, Core/Features/SchemataAuthenticationFeature.cs, cache-owned files).
No build/test was run (Lead owns builds). No tests/docs touched; only this report was written under `docs/`.

Result: **0 forced-access null-forgiving sites** (`x!.y` / `x![i]`) left in scope.
29 forbidding initializers removed (28 `null!`, 1 `default!`); 52 remain as documented
runtime-contract exceptions (50 `null!`, 2 `default!`). 25 files changed.

## 1. Forced-access replacements (21 sites, 9 files)

| File | Change |
|---|---|
| `src/Schemata.Common/ResourceNameDescriptor.cs` | `Resolve()`: `seg.Property!.Underscore()/Humanize()` replaced by one `if (seg.Property is not { } propertyName) throw new MissingFieldException(type.Name, seg.Raw);` then use `propertyName` (single evaluation). Also `private readonly Type _type = null!;` → uninitialized readonly field (always assigned in the ctor before any return). |
| `src/Schemata.Core/Configurators.cs` | `Set<T>`/`Set<T1,T2>`: `configure!.Invoke(...)` replaced by direct `_configurators.TryGetValue(key, out var existing) && existing is Action<T> configure` chaining. Same lookup key/semantics as `TryGet`, no new annotation needed. |
| `src/Schemata.Core/Extensions/ServiceCollectionExtensions.cs` | Rate-limiter `OnRejected`: `error.Details!.Add(...)` → `(error.Details ??= []).Add(...)`. |
| `src/Schemata.Core/Building/ResourceRegistry.cs` | `Install`/`ApplySecurity`/`ReplaySecurity`: `_wiring!` replaced by one narrowed local `if (_wiring is not { } wiring) throw new InvalidOperationException(...)`; `ApplySecurity` now receives the non-null wiring parameter. 7 sites. |
| `src/Schemata.Actor.Foundation/Runtime/ActorInstance.cs` | `slot.Timer!.Dispose()` ×3 → `slot.Timer?.Dispose()` (timer is always assigned before the slot enters `_timers`; `?.` is the disposal contract). `persistent!` ×2 → `persistent is { } persistentActor` / `stateWriter` guards inside the state-store branches. |
| `src/Schemata.Flow.Scheduling/Handlers/FlowTimerCatchHandler.cs` | `process.CanonicalName![...]` (×2, evaluated twice) → `if (process.CanonicalName is not { } canonicalName) throw new InvalidOperationException(...)` then slice the local. |
| `src/Schemata.Mapping.AutoMapper/AutoMapperConfigurator.cs` | `method!` → `if (method is null) throw new MissingMethodException(typeof(...).FullName, nameof(Map));`. |
| `src/Schemata.Mapping.Mapster/MapsterConfigurator.cs` | Same as AutoMapper. |
| `src/Schemata.Transport.Grpc/Extensions/ServicesExtensions.cs` | `.Where(m => m is not null).Select(m => m!.ServiceType)` → `.OfType<GrpcMethodMetadata>().Select(m => m.ServiceType)`. |

## 2. `null!` / `default!` removals (29)

- `src/Schemata.Common/ResourceNameDescriptor.cs` — `_type = null!` (ctor initializes it).
- `src/Schemata.Actor.Foundation/Runtime/ActorRegistry.cs` + `src/Schemata.Actor.Skeleton/IActorRegistry.cs` —
  `TryResolve(string, out Props props)` + `props = null!` → `[NotNullWhen(true)] out Props? props`
  (interface + impl); the single caller (`InProcessActorSystem.GetAsync`) drops its `out props!`.
- `src/Schemata.Expressions.Aip/AipCompileVisitor.cs` — `TryBuildRepeatedHas` `expression = null!` and
  `TryAccess`'s `expression = access!` removed via `[NotNullWhen(true)] out Expression?` + `null` on
  false paths; `TryResolveIdentifier` became `ResolveIdentifier(...) : Expression?`, matched with
  `is { } access` in `VisitValue` (no nullable-out reliance in a switch arm).
- `src/Schemata.Expressions.Cel/CelCompileVisitor.cs` — 5 sites: `TryGetDictionaryKeyType`,
  `TryGetDictionaryContainsKey` (both outs), `TryNumericPromotion`, `TryAccess` now use
  `[NotNullWhen(true)] out T?` with `null` on false paths; the interface `ContainsKey` lookup uses
  `is not { } interfaceMethod → continue` instead of `GetMethod(...)!`.
- `src/Schemata.Expressions.Skeleton/ExpressionCache.cs` — `LruCache.TryGet` `value = default!` →
  `[NotNullWhen(true)] out TValue? value` (private nested type, no in-repo callers).
- `src/Schemata.Flow.Skeleton/Runtime/ProcessRegistration.cs` — `Name`, `Definition` → `required`.
- `src/Schemata.Flow.Skeleton/Models/ProcessConfiguration.cs` — `Name` → `required`.
- `src/Schemata.Flow.Skeleton/Models/ProcessDefinitionInfo.cs` — `Version` → `required`.
- `src/Schemata.Flow.Skeleton/Runtime/FlowConditionContext.cs` — `Definition`, `Token`, `CurrentState`
  → `required` (both in-repo construction sites set all members; `Execution` was already `required`).
- `src/Schemata.Flow.Skeleton/Observers/FlowTransitionContext.cs` — `Snapshot` → `required`.
- `src/Schemata.Flow.Skeleton/Runtime/LambdaConditionExpression.cs` — nullable backing field +
  `required Lambda` property; getter throws `InvalidOperationException` on the impossible unset path.
- `src/Schemata.Flow.Event/Events/{ProcessStartedEvent,ProcessCompletedEvent,ProcessFailedEvent,
  TransitionMadeEvent}.cs` — 11 init-only event payload members → `required` (all construction sites
  are the lifecycle observer, which sets every member).

## 3. Remaining `null!` / `default!` exceptions (52) and why

**EF entity materialization contract (25)** — entities are instantiated by the ORM and populated
through setters; non-nullable column members are conventionally `null!` until materialized, and
in-repo tests construct them with object initializers, so `required` would be an API break for a
mapper contract this sweep does not own:
- `src/Schemata.Actor.Skeleton/Entities/SchemataActor.cs` (2)
- `src/Schemata.Event.Skeleton/Entities/SchemataEventSubscription.cs` (3)
- `src/Schemata.Flow.Skeleton/Entities/`: `SchemataProcess` (2), `SchemataProcessToken` (3),
  `SchemataProcessTransition` (1), `SchemataProcessCompensation` (4), `SchemataProcessParticipant` (2),
  `SchemataProcessSource` (4), `SchemataFlowEffectIntent` (4) — 20 total.

**BPMN AST / parser-builder contract (20)** — model elements are constructed empty and populated
through `EnsureMutable()`-guarded setters while the graph is being built/parsed, then frozen.
Partial construction is part of that contract (e.g. `new TimerDefinition { TimerType, TimeExpression }`,
`new ProcessDefinition()` without a name), so `required` cannot express setter-based population and
would break parser/builder/test call sites:
`FlowElement.Name` (1), `TimerDefinition` (2), `ErrorDefinition` (2), `ConditionalDefinition` (2),
`SequenceFlow` (2), `CancelDefinition` (1), `LinkDefinition` (1), `Message` (1),
`MultipleDefinition` (1), `NoneDefinition` (1), `EscalationDefinition` (1), `CompensationDefinition` (1),
`CallActivity` (1), `ParallelDefinition` (1), `Signal` (1), `ProcessDefinition.Name` (1).

**Wire/request DTO deserialization contract (3)** — HTTP/gRPC request bodies are deserialized from
JSON/protobuf; making the members `required` would turn a missing field into a serializer failure
instead of the existing validation/protocol behavior:
`Flow.Skeleton/Models/ThrowSignalRequest.SignalName`, `CorrelateMessageRequest.MessageName`,
`StartProcessInstanceRequest.DefinitionName`.

**Test-constrained runtime members (2)** — production code always sets them, but existing tests
(out of scope to edit) construct the type without the member, so `required` cannot be added:
`ProcessRegistration.Engine` (source-projection test registration omits it and never resolves an
engine), `FlowTransitionContext.Execution` (two transition-context test helpers omit it for advisors
that do not read it).

**Framework/interface contracts (2)**
- `Messaging.Skeleton/Runtime/InProcessStreamDispatcher._current = default!` —
  `IAsyncEnumerator<TItem>.Current` for an unconstrained `TItem` has no non-null default before the
  first `MoveNextAsync`; a nullable field cannot be returned as `TItem` without reintroducing a
  forbidding operator.
- `Transport.RabbitMq/CorrelationTracker` `tcs.TrySetResult(default!)` — broker reply body is `null`
  for an unconstrained `TResponse`; `default!` is the intentional mapping of a null payload.

## 4. Risks / notes for the Lead

- **Public API annotations/constraints added** (source-level, not shape-except-`required`):
  `required` on `ProcessRegistration.Name/Definition`, `ProcessConfiguration.Name`,
  `ProcessDefinitionInfo.Version`, `FlowConditionContext.Definition/Token/CurrentState`,
  `FlowTransitionContext.Snapshot`, `LambdaConditionExpression.Lambda` and the four Flow event types;
  `[NotNullWhen(true)]` on `IActorRegistry.TryResolve`. Every in-repo (src + tests) object-initializer
  site was checked to set the added `required` members; runtime serializers/ORM are unaffected by
  `required` (compile-time only), but external consumers constructing these types would need updates.
  Explicitly out of scope to edit: the tests that would need updating if `ProcessRegistration.Engine`
  / `FlowTransitionContext.Execution` were made `required`.
- **Behavior changes only on previously-NRE paths**: `ResourceRegistry` wiring invariant now throws
  `InvalidOperationException`; `FlowTimerCatchHandler.JobName` throws `InvalidOperationException` for a
  process without a canonical name; `LambdaConditionExpression.Lambda` getter throws instead of
  returning null; a failed interface `ContainsKey` reflection lookup falls back to
  `Enumerable.Contains`; AutoMapper/Mapster missing-method now `MissingMethodException`.
- **Not addressed by design** (narrowed scope): ordinary argument/statement `x!` suppressions
  (`process.Name!`, `GetMethod(...)!`, `GetService<T>()!`, `return x!`) and `GetService`→
  `GetRequiredService` conversions remain; only forced access and `null!`/`default!` initializers were
  in scope. Cache-owned files, `Core/State/**`, `Flow.Foundation/Repository/**`, `Flow.Repository` /
  `Scheduling.Repository` and `SchemataAuthenticationFeature.cs` were left untouched.
- Verification: `grep -rnE '[A-Za-z0-9_)\]]!\.'` over the scope returns 0; a multiline forced-access
  scan (`!` at end of line followed by `.`/`[`) also returns 0. Compile risk is compile-only in the
  amended files; no runtime behavior other than the impossible-path throws above.
