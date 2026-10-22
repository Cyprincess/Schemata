# Nullable sweep — group B (#379 / Task 18)

Scope: group B projects under `src/Schemata.{Security.*,Authorization.*,Identity.*,Scheduling.*,
Entity.*,Resource.*,Tenancy.*,Insight.*,Report.*,Push.*,Scim}/**`, with the task-8 exclusions:
`Tenancy.Foundation/State/**`, `Tenancy.Skeleton`, `Scheduling.Foundation/Repository/**`,
`Scheduling.Repository/**` (deleted by the package merge) and the task-6 cache-owned files
(`Insight.Foundation/Materialization/PublicModel.cs`, `Authorization.Foundation/Services/RequestObjectReader.cs`,
`Entity.EntityFrameworkCore/SchemataModelCustomizer.cs`, `Entity.Repository/DatabaseErrorClassifier.cs`,
`Entity.Repository/RepositoryBase.cs`, `Resource.Http/Linking/ResourceLinkContributor.cs`).
No build/test was run (Lead owns builds). No tests/docs touched; only this report was written under `docs/`.

Result: **0 forced-access null-forgiving sites** (`x!.y` / `x![i]`) left in the scope.
44 forced-access occurrences (43 scanner hits; one line carried two) replaced in 27 files,
**4 of 22 `null!`/`default!` sites removed**; 18 remain as documented runtime/contract exceptions.
33 files changed.

## 1. Forced-access replacements (files changed, what changed)

| File | Change |
|---|---|
| `Authorization.Foundation/Services/ClientSecretJwtAuthentication.cs` | `form![…][0]!` → `if (form?[Parameters.ClientAssertion] is not [{ } assertion]) return null;` (Presents already guarantees one non-blank assertion; single evaluation). |
| `Authorization.Foundation/Services/PrivateKeyJwtAuthentication.cs` | Same as above. |
| `Authorization.Foundation/Services/AuthorizationDetailsService.cs` | `Narrow`: `request!["type"]!.GetValue<string>()` → guard `if (request?["type"]?.GetValue<string>() is not { } name) throw OAuthException(InvalidRequest, …TYPE_MEMBER)`; `candidate!["type"]` → `candidate?["type"]` (Parse already guarantees both, guard is defensive). |
| `Authorization.Foundation/Services/DefaultOpSessionService.cs` | `slot!.Value` → `slot is not { } onlineSlot` in the same `||` chain (online evidence already implies non-null); `principal!.FindFirstValue(...)` ×2 → `if (principal is { } hostPrincipal && HasHostAuthenticationBasis(hostPrincipal))`. |
| `Authorization.Foundation/Services/RegistrationMetadataMapper.cs` | `request.RedirectUris!` after null guard → captured local `redirectUris` (member state is invalidated by the interleaved awaits); `ValidateUriHostConsistency`: `RedirectUris!` → `if (request.RedirectUris is not { } redirectUris) throw OAuthError(InvalidRedirectUri, …REDIRECT_URIS_REQUIRED)` (same precondition as `ValidateRedirectUris`). |
| `Authorization.Foundation/Handlers/EndSessionHandler.cs` | `application!.CanonicalName!` → early `if (application is null) throw OAuthException(InvalidRequest, …)` after the strict resolve (strict mode guarantees it) + `application.CanonicalName ?? string.Empty`; `evidence!.Target` → `if (evidence is not { MatchesCurrent: true })` early-return branch, then direct `evidence.Target` (the old `current` local was only used for that branch). |
| `Scheduling.Foundation/JobExecutionLease.cs` | `execution.LeaseExpireTime!.Value` ×2 → `if (execution.LeaseExpireTime is not { } leaseExpireTime) throw new InvalidOperationException(...)` then use the local twice. |
| `Scheduling.Foundation/JobExecutionDispatcher.cs` | `context!.Execution?.Output` → `context?.Execution?.Output` (context is always built before the step that reaches this line). |
| `Scheduling.Foundation/Settlement/JobSettlement.cs` | 3× `persisted!` → `persisted is not null` added next to the existing `owned` test in the same condition (`owned` already implies it): `owned && persisted is not null ? …`, `if (owned && persisted is not null) { … }`, and the rearm test. |
| `Scheduling.Skeleton/ScheduleDefinitionMapper.cs` | `job.IntervalTicks!.Value` / `job.CronExpression!` → property patterns with `ArgumentOutOfRangeException(nameof(job), …)` in the switch arms, matching the existing OneTime arm style. |
| `Entity.EntityFrameworkCore/EfCoreRepository.cs` | `_unitOfWork!.Admit(entry)` → `if (_unitOfWork is not { } unitOfWork) throw new InvalidOperationException(...)`; single evaluation. |
| `Entity.Repository/Estimation/QueryPlanEstimate.cs` | `command.Connection!` (arg) and `command.Connection!.CreateCommand()` → one guarded local `if (command.Connection is not { } connection) throw new InvalidOperationException(...)` inside the SqlServer branch only (other providers do not need the property). |
| `Identity.Skeleton/Stores/SchemataUserStore.cs` | `entry!.Value` → `if (entry is not { Value: { } stored }) return false;` then reuse `stored` for the split (old null chain produced the same false result). |
| `Identity.Foundation/SchemataSignInManager.cs` | `_request!.UseCookies == true` → `_request is { UseCookies: true }` (null request means cookie sign-in is not requested). |
| `Identity.Scim/Controllers/ScimUsersController.cs` | Create: `user.Meta!.Location!` → `Location("Users", user.Id)` (same deterministic URL that `WithLocation` writes); `WithLocation`: `user.Meta! with` → `if (user.Meta is not { } meta) throw new InvalidOperationException(...)` then `meta with`. |
| `Identity.Scim/Controllers/ScimGroupsController.cs` | Same for Groups (`Location("Groups", group.Id)` / `group.Meta`). |
| `Insight.Foundation/Execution/GroupKey.cs` | `dictionary!.TryGetValue` → `if (ordinalLookup && dictionary is { } lookup)` then `lookup.TryGetValue` (ordinalLookup already implies non-null). |
| `Insight.Foundation/Execution/LocalPipelineExecutor.Materialize.cs` | `.Where(...).Select(item => … item.Expression! …)` LINQ chain → single `foreach` with `if (item.Kind is not SelectionKind.Expression || item.Expression is not { } expression) continue;`, filling the same typed `List<(string Alias, Func<…> Value)>`; removed the now-unused `using System.Linq;`. |
| `Insight.Foundation/Execution/PlanExecutor.cs` | `scanCap!.Value` → `if (scanCap is not { } scanLimit) throw new InvalidOperationException(...)` before buffering; `LocalScanCap` returns null only when there are no local stages, so this is the existing invariant. |
| `Insight.Foundation/Planning/PublicPlanValidator.cs` | `item.Expression!` / `item.FieldPath!.Split('.')` → `if (item.Expression is not { } expression) throw Invalid(alias)` and `if (item.FieldPath is not { } fieldPath) throw Invalid(alias)` then use the locals. |
| `Insight.Skeleton/Models/InsightValueModel.cs` | `value.GetString()![0]` → `value.GetString()?[0]` (a `String`-kind `JsonElement` never yields null; the null-conditional is behaviour-identical for reachable inputs). |
| `Push.Grpc/PushControlGrpcService.cs` | 4× `context.ServerCallContext!.GetHttpContext().User` → shared `private static ClaimsPrincipal Caller(CallContext)` that throws `InvalidOperationException` when the client-side context has no `ServerCallContext`. |
| `Push.WebPush/WebPushEncryption.cs` | `parameters.Q.X!/Y!` → `if (parameters.Q.X is not { } x \|\| parameters.Q.Y is not { } y) throw new CryptographicException(...)` then copy the locals. |
| `Resource.Foundation/Advisors/AdviceApplyChildParent.cs` | `child.Parent!.Split('/')` (and 3 later reads) → `var parent = child.Parent;` null/blank guard on the local (single getter read) then use `parent`. |
| `Resource.Foundation/PurgeJob.cs` | `unit!.CommitAsync` → `if (unit is not { } transaction) throw new InvalidOperationException(...)` (non-forced path returns before this line). |
| `Resource.Foundation/ResourceOperationHandler.List.cs` | `resolved!.MaxResidualScanRows` → `if (resolved is not { } language) throw new InvalidOperationException(...)` in the residual branch (`residual != null` implies the filter language was resolved). |
| `Resource.Grpc/ResourceServiceMethodProvider.cs` | `GetMethod(...)!.MakeGenericMethod(...)` → `var method = …GetMethod(…) ?? throw new InvalidOperationException(...)` then `method.MakeGenericMethod(...)`. |
| `Resource.Http/ResourceMethodControllerFeatureProvider.cs` | `Registry!.GetMethods(...)` inside `Registry?.Resources ?? []` loop → early `if (Registry is not { } registry) return;` then use `registry` throughout. |
| `Tenancy.Foundation/Services/TenantQuotaOptions.cs` | `error.Details!.OfType<…>()` → `if (error.Details is { } details)` (also replaced the sibling `detail.Violations!` with an `is not { }` skip). |
| `Tenancy.Messaging/TenantMessageExecutionScopeFactory.cs` | `final!.Value` → `if (final is not { } scope) throw new InvalidOperationException(...)` after the failure path throws (a successful create always assigns it). |

## 2. `null!` / `default!` removals (4)

- `Tenancy.Foundation/Services/TenantResolutionContext.cs` — `Services { get; set; } = null!` →
  `internal required IServiceProvider Services { get; set; }`; the single in-repo construction site
  (`SchemataTenantServiceProviderFactory.Build`) initializes it, and `CompositeScope` still assigns the
  composite scope later (allowed for a `required` member).
- `Tenancy.Foundation/Services/SchemataTenantServiceProviderFactory.cs` — `TenantCompositeServiceProvider
  composite = null!;` placeholder for the deferred descriptor closures → `TenantCompositeServiceProvider?
  composite = null;` plus a local `IServiceProvider Composite()` that throws
  `InvalidOperationException` if a wrapped tenant descriptor is resolved before the provider is built
  (unreachable: the container is built before any scope/service resolution); the scoped
  `TenantResolutionContext` factory now uses `Composite()`.
- `Tenancy.Foundation/Services/TenantCompositeServiceProvider.cs` — `IsEnumerableService(…, out Type
  elementType)` `elementType = null!` → `[NotNullWhen(true)] out Type? elementType` with `null` on the
  false path (internal helper; both callers discard the out value with `out _`).
- `Scheduling.Foundation/JobExecutionLease.cs` — `return default!;` in the generic no-transition sentinel
  → added `where T : struct` to the internal `TransitionAsync<T>` and `return default;` (all call sites
  use `bool` or the `SettlementState` struct: JobSettlement, JobExecutionDispatcher/DefaultOperationService
  via the `Task<bool>` overload, and the scheduling tests).

## 3. Remaining `null!` / `default!` exceptions (18) and why

**EF Core entity materialization + ASP.NET Identity base-class contract (7)** — these are `override`s of
`IdentityUser{Login,Token,Role}<string>` pseudo-entities. The ORM instantiates them and populates the
required columns through setters; the base declarations are non-nullable strings, and C# does not allow
`required` on an override, while initializing with `string.Empty`/nullable annotations would change the
persistence/API contract:
`Identity.Skeleton/Entities/SchemataUserLogin.cs` (2), `SchemataUserToken.cs` (3),
`SchemataUserRole.cs` (2).

**Wire request DTO deserialization/model-binding contract (8)** — ASP.NET Core JSON/model binding
constructs the DTO and then sets values; the members are settable (not init-only) with `[Required]`
validation, so `required` would turn a missing/nulled field into a serializer failure instead of the
existing validation/protocol behavior, and nullable annotations would weaken the wire contract:
`Identity.Skeleton/Models/LoginRequest.cs` (2), `RegisterRequest.cs` (2), `ResetRequest.cs` (2),
`ConfirmRequest.cs` (1), `RefreshRequest.cs` (1).

**Password-hasher interface contract (2)** — `Security.Foundation/Services/SecretVerifier.cs` passes
`null!` as `TUser` to `IPasswordHasher<SchemataSecurity>.HashPassword/VerifyHashedPassword`. The interface
requires a non-null `TUser` instance, but the hash/verify operations have no security row at hand (hash)
and the default hasher ignores the user; fabricating a `SchemataSecurity` instance would give the hasher
phantom entity state, so this is a deliberate runtime contract suppression.

**Definite-assignment cleanup wrapper (1)** — `Tenancy.Actor/TenantActorSystem.cs` `Reference.AskAsync`:
`TResponse response = default!;` is assigned only on the success path, then two cleanup steps run and any
failure (original or cleanup) is aggregated and rethrown; the success value is returned after the throws.
Removing it needs either a nullable local plus a return-time `!` (no suppression win) or restructuring
cleanup into a `finally`, which would change the nested `AggregateException` composition the class relies
on — flagged rather than guessed (see risks).

## 4. Risks / notes for the Lead

- **Internal signature changes (source-level, no public API shape):**
  `JobExecutionLease.TransitionAsync<T>` gained `where T : struct` (internal class/method; verified all
  src + test call sites use `bool`/`SettlementState`); `TenantResolutionContext.Services` is now
  `required` (internal type, one construction site updated); `IsEnumerableService` is now
  `[NotNullWhen(true)] out Type?` (internal, callers use `out _`). `required` only affects compile-time
  object creation.
- **Defensive throws replace only previously-NRE paths (unreachable by current invariants):** lease
  constructor without `LeaseExpireTime`, EF repository without attached UoW, purge commit without UoW,
  residual page without resolved language, SQL Server plan command without connection, periodic/cron job
  without its persisted timing fields, EdSession hint with a null application, tenant message scope not
  created, SCIM resource without metadata, Insight local pipeline without scan cap, web-push key without
  public point (now `CryptographicException`), malformed `authorization_details` element that bypassed
  `Parse`, host-consistency validation without redirect URIs, gRPC push call without server call context,
  nil-`CallContext`.
- **Single-read/refactor notes:** `AdviceApplyChildParent` reads `IChild.Parent` once instead of up to
  four times (DTO auto-property in-repo; a side-effecting custom getter would observe the difference);
  `RegistrationMetadataMapper` captures `RedirectUris` in a local after the null guard (same list);
  `LocalPipelineExecutor.Materialize.Select` LINQ chain became an equivalent ordered `foreach`;
  `JobSettlement` adds redundant `persisted is not null` tests next to `owned` (logically identical);
  `PushControlGrpcService` funnels the four calls through one helper.
- **Encoding/style:** preserved the original UTF-8 BOM on the two scheduling files whose rewrite dropped
  it; `git diff --check` is clean. No unused usings added (`System.Linq` removed from one file).
- **Verification (no build):** a scope scanner over `src/` with the nullable regex
  `!\s*[.\[]` (covers `x!.y`, `x![i]`, and `!` followed by a line break before `.`/`[`) returns **0**
  for every group-B project, including the excluded subdirectories; `\b(null|default)!` returns the 18
  documented sites above. `git diff --check` and a comment/string-stripped delimiter-balance smoke test
  over all 33 edited files pass; final compile/behavior verification is the Lead's build.
