# Lead notes — 2026-10-20 review round

Baseline: `00c34ae07..HEAD 90ff2a5f` (93 commits); working tree carries commits `89002972` and `c44090aa`.

## CI analysis

### `build` workflow job 118435010264 (the linked failure)

The job failed at the first cross-cutting warning promoted to an error:

1. `generators/Schemata.Advice.Generator/AdvicePipelineGenerator.cs(15,9): error RS2008` — the generator project declares analyzer rule `SKA001` but has no `AnalyzerReleases.Shipped.md`/`AnalyzerReleases.Unshipped.md`, unlike `Schemata.Modeling.Generator`. `Directory.Build.props` sets `EnforceExtendedAnalyzerRules=true` for `generators/**`; Arcade's `-ci` build sets `TreatWarningsAsErrors=true`, so the warning is fatal. The same root cause failed the `docs` workflow job.
2. After fixing (1), the same build uncovered 62 further latent warnings-as-errors that the generator failure had masked: `CS1712`/`CS1574` XML-doc tags and crefs in `Schemata.Resource.Foundation/Advisors` (added with the explicit-payload refactor `10122728`), `CS1574` crefs in `Schemata.Flow.Repository`, `CS8602`/`CS8604`/`CS8631` nullable warnings in `Schemata.Resource.*` test projects, and `xUnit2029`/`xUnit2031` analyzer findings in `Schemata.Tenancy.Integration.Tests`.

Verification: `dotnet build Schemata.slnx -c Release /p:TreatWarningsAsErrors=true` → 0 warnings, 0 errors.

### `analysis` workflow job 118435011012 (SonarQube)

`Schemata.Tenancy.Integration.Tests` net8.0 fails three job/reminder/scheduling tests with:

`System.InvalidOperationException : TenantJobExecution requires IResourceTypeResolver for ResourceReferenceAttribute.`

Root cause: commit `35d57331` made `[ResourceReference]` validation require `IResourceTypeResolver` unconditionally, even for *typed* references whose target type is declared on the attribute. Framework-internal entities (`SchemataJobExecution.Job` → `SchemataJob`, Flow process entities) therefore require a resource-layer service in repository-only hosts; the tenancy repository+Scheduling host has no such registration, so persisting an execution row throws. Registering a resolver in the tests would have been a test-only workaround for a production break (the framework ships no resolver in `Entity.Repository`; `DefaultResourceTypeResolver` lives in `Resource.Foundation` and needs `ResourceRegistry`).

Fix `c44090aa`: typed references are validated without a resolver against the declared target's own `[CanonicalName]` pattern (`ResourceNameDescriptor.ParseCanonicalName`); a registered resolver additionally enforces assignability so host-substituted patterns still work; polymorphic references keep the resolver contract and now fail at the declaring property (configuration error). `AdviceValidateResourceReferenceExistence` queries the declared target repository directly for typed references. Verification: `Schemata.Entity.Repository.Tests` 81/81, `Schemata.Tenancy.Integration.Tests` 157/157, `Schemata.Scheduling.Integration.Tests` 31/31, `Schemata.Flow.Integration.Tests` 174/174 on both TFMs.

## Lead cross-cutting findings

| ID | Type | Sev | Area | Evidence | Issue |
|----|------|-----|------|----------|-------|
| L-01 | D | P1 | Entity.Repository references | Typed `[ResourceReference]` required `IResourceTypeResolver`; framework-internal Scheduling/Flow entities then fail in repository-only hosts (CI test failure). Fixed in `c44090aa`. | #359 |
| L-02 | P | P1 | Component isolation | `src/Schemata.Transport.Grpc/Schemata.Transport.Grpc.csproj` references `Schemata.Security.Foundation`; `Extensions/GrpcSignatureMessageExtensions.cs` adapts gRPC metadata into `Schemata.Security.Foundation.Signatures.SignatureMessage`. A transport component must not depend on the security component; the gRPC signature bridge belongs to a Security transport bridge (taxonomy `Schemata.Security.Grpc`) or behind an abstraction owned by Transport. | new |
| L-03 | P | P2 | Component isolation | `src/Schemata.Authorization.Foundation/Schemata.Authorization.Foundation.csproj` references `Schemata.Transport.Http` (used for `[DependsOn<SchemataTransportHttpFeature>]`) and `Schemata.Scheduling.Skeleton` (token cleanup / back-channel logout jobs embedded in Foundation). Other components carry bridges for this (`Report.Scheduling`, `Flow.Scheduling`, `Push.Scheduling`, `Actor.Scheduling`); Authorization has none and instead couples Foundation to the transport component and embeds the scheduling bridge. | new |
| L-04 | C | P2 | Public naming | `Schemata.Core.Features.SchemataAuthorizationFeature` (ASP.NET `UseAuthorization()` middleware) and `Schemata.Authorization.Foundation.Features.SchemataAuthorizationFeature<...>` (OAuth authorization server) are two public types with the same full simple name and unrelated responsibilities. | new |
| L-05 | C | P3 | Public naming | `Schemata.Common.ResourceIdentifiers` (request predicates) and `Schemata.Resource.Http.Runtime.ResourceIdentifiers` (route→canonical-name builder) collide by name with different responsibilities; the HTTP one is transport-only code. | new |
| L-06 | X | P3 | Test infrastructure | `IResourceTypeResolver` stand-ins are duplicated across test projects (`Schemata.Authorization.Integration.Tests/Fixtures/TestResourceTypeResolver.cs`, `Schemata.Scheduling.Integration.Tests/Fixtures/SchedulingTestResources.cs`, `Schemata.Entity.Repository.Tests/.../LogicalReferenceShould`, plus inline registrations in Push/Report/Flow/Actor fixtures). After `c44090aa` only polymorphic-reference hosts still need one; consolidate into one test fixture or delete the now-unneeded ones during the next test pass. | #359 |
| L-07 | O | P3 | Framework hygiene | Stale working directories `src/Schemata.Caching.Distributed/` and `src/Schemata.Entity.Owner/` contain only generated `FodyWeavers.xsd` (untracked, gitignored) — workspace residue, not a repository defect. `#343` items are otherwise fixed (Tenancy.Skeleton reference removed, AGENTS.md says 90 packages, `ResponseTypeRequirements` moved to `AuthorizationConstants`, mapper surface reduced). | #343 |

## Package taxonomy (reviewed, no merge proposed)

- 90 `src/*` projects; thinnest are bridge/adapter packages by design (`Validation.Skeleton` 18 lines, `Identity.Grpc`/`Http`, `Authorization.Grpc`, `Scheduling.Grpc`, `Domain.Skeleton` 76 lines). These are contract/adapter boundaries, not accidental packages.
- Three RabbitMQ packages are distinct responsibilities: `Transport.RabbitMq` (connection provider), `Event.RabbitMq` (event bus producer/consumer), `Messaging.RabbitMq` (request dispatcher bridge). No merge.
- `Schemata.Scim` (shared RFC 7643/7644 protocol) + `Schemata.Identity.Scim` (identity adapter) is a correct split; the missing `Authorization.Scim` is a capability gap tracked by #126, not a package to create speculatively.
- `Expressions.Order` is a stateless single-provider adapter registered through DI; justified by the provider convention.

## CI fix commit series (pushed to both remotes)

| Commit | Scope | Verification |
|---|---|---|
| `89002972` | Advice generator analyzer release tracking (`RS2008`); XML-doc typeparams/crefs; nullable + xUnit analyzer findings (62 latent warnings-as-errors) | `dotnet build Schemata.slnx -c Release /p:TreatWarningsAsErrors=true` 0W/0E |
| `c44090aa` | Typed `[ResourceReference]` validation without a resolver; polymorphic refs keep the resolver contract; existence validation queries the declared target repository | Entity.Repository 81/81, Tenancy.Integration 157/157, Scheduling.Integration 31/31, Flow.Integration 174/174 (both TFMs) |
| `8d83609b` | macOS deterministic failure: ECDSA curve test used secp256k1 (Apple backend rejects it) → P-521; Actor.Scheduling reminder test deadline 5s → 30s | Security.Tests 272/272 (both TFMs); Actor.Scheduling.Tests 4/4 |
| `692bde15` | Actor.Foundation.Tests 5s lifecycle waits → 30s (Windows lane flake) | Actor.Foundation.Tests 70/70 (both TFMs) |
| `a73624f7` | All test `WaitAsync`/`CancellationTokenSource`/`ManualResetEventSlim.Wait` 5s failure budgets → 30s; semantic 5s values untouched | Full Release build 0W/0E; Tenancy.Integration/Tenancy/Scheduling/Event.Foundation suites green |

Final CI state: on `a73624f7` **all three workflows are green** — `build` (ubuntu-latest, macos-latest, windows-latest, publish), `docs` (Build Documentation + Deploy to GitHub Pages) and `analysis` (Cocogitto + SonarQube with the full test run). Exact Windows failure evidence before the fix: `ActorShutdownShould.Ask_Cancellation_Returns_While_Item_Callback_Blocks_And_Retirement_Waits`, `System.TimeoutException`, 5.16s at `actor.Completion.WaitAsync(5s)` (`ActorShutdownShould.cs:239`), recovered from the CI trx artifact. macOS evidence: `HttpMessageSignerShould.Support_Only_The_Named_Curve_For_The_Ecdsa_Algorithms` failing with `PlatformNotSupportedException` for secp256k1 (122 macOS trx contained exactly this one failure).

## Closed-issue cross-analysis (added after user note)

- Gitea closed count at round start: 296; full MCP fetch returned 321 (the 25 closed by this round included); after reopens: open 47 / closed 319.
- Reopened 2 closed issues whose own acceptance is contradicted by HEAD: #24 (tenant cache/reservation framing — tenant scopes fall back to the unframed host provider, idempotency key has no tenant) and #135 (validated callback context is bypassed on the interactive success path).
- Commented with HEAD evidence on 24 closed issues (#2 #3 #20 #24 #39 #66 #97 #135 #139 #145 #146 #188 #192 #195 #199 #220 #222 #236 #263 #264 #273 #275 #318 #333, two of them reopened), each pointing at the open successor/plan task instead of opening duplicate tracking.
- Added closed-issue predecessor references to all 12 new issues (#361–#372).
- Analysis and lessons: spec §11; ledger note "Closed issue 交叉分析（补充轮）".

## Owner feedback round 2 (2026-10-20)

| Item | Verdict | Action |
|---|---|---|
| `IFilterRequest` | dead: no implementation in `src/`, only a test fake; `ApplyFilter` only self-tested | removed with its helper/test in `fefd7bae` (#374) |
| `ValidateExistence` / write-time reference checks | owner decision: framework performs none | both advisors + option + registrations/tests/docs removed in `fefd7bae` (#373); `AdviceUpdateTenant` re-anchored; #[359/#369/#39 updated] |
| `AppDomainTypeCache` | valuable (20+ hot paths, type metadata only) | keep + harden late-loaded visibility, route raw property/field reflection through it (#378) |
| `Schemata.Core/State` | confirmed intrusive tenancy client/UA isolation: only consumer is Tenancy, Core replaces `IAuthenticationService` and branches session/antiforgery | move into Tenancy; Core returns to native registration (#376, plan Task 16) |
| `Flow.Repository` / `Scheduling.Repository` | thin bridge packages (2 files each) over `Entity.Repository`, which Foundations already reference | fold into their Foundations, 90→88 packages (#375, plan Task 15) |
| null-forgiving `!` | 69 forced-access + 477 masking + 103 `null!`/`default!` in `src/**` | remove forced access via pattern-matching/early-return, then masking/initializers; decide analyzer guard (#379, plan Task 18) |

## Owner feedback round 2 — final state

- Implemented: `fefd7bae` removed write-time `[ResourceReference]` validation (advisors + `ValidateExistence` + registrations/tests/docs; `AdviceUpdateTenant` re-anchored) and the dead `IFilterRequest`/`ApplyFilter` surface; `70d7aafb` removed the Flow tenant test that asserted the removed validation and kept the fixture resolver helper (Flow source loading still needs it).
- Verified: Release build 0W/0E; Entity.Repository 57/57, EF Core integration 98/98, Resource 316/316, Tenancy 100/100 + Integration 157/157, Flow.Integration 172/172 (net8.0/net10.0).
- CI on `70d7aafb`: build + docs + analysis all green.
- Planned/created: #375 package fold, #376 Core state de-intrusion, #378 AppDomainTypeCache keep+harden+reflection routing, #379 null-forgiving sweep; #373/#374 done+closed, #377 duplicate closed, #369 superseded closed.
- Note: gitea `raid.tekoki.internal` became unreachable during the final push window; GitHub remote is up to date and green, gitea push retries in the background.

## Owner-requested completion round 2 (2026-10-21)

Commits (GitHub pushed; gitea unavailable at push time):
- `06d7f3a9` #375 fold Flow/Scheduling repository adapters into their Foundations (90→88 packages)
- `c4ba6f18` #376 move tenancy client state-profile auth routing out of Core; Core keeps only the generic state-pipeline seam and native authentication registration
- `6fa8b7df` #378 keep/harden AppDomainTypeCache (incremental refresh, GetAllTypes/GetFields), route System.Type reflection, tests
- `4ed3f1d2` #379 zero forced-access null-forgiving in src/**, null!/default! cleanups + AGENTS.md rule; ExpressionCache generic constraint fix

Verification: Release build 0W/0E; broad net8.0 suite green (Core 106, Tenancy 106, Tenancy.Integration 157, Flow 235/172, Scheduling 165/31, Entity 57/98/87, Insight 105/57/19, Mapping 5/19/27, Authorization 1113/292, Push 66/9/55, Report 139/92, Resource 316/98/45, Security 272, Event 32, Actor 70, Messaging 33, CEL 1124, AIP 148, Common 65). CI: GitHub run on `4ed3f1d2`.

## CI flake on the completion round (2026-10-21)

First CI run on `4ed3f1d2`: build/docs/analysis — docs and analysis green, build red at
`Schemata.Tenancy.Integration.Tests` net10.0 (`TenantActorShould.Same_Type_Key_Uses_Separate_Objects_State_And_Bound_Lifecycle_Identity`).
Root cause: the test shared one in-memory SQLite connection across actor turns and assertions, and the tenant runtime's terminal
notification could create its turn scope after the owning provider had disposed (container disposal order is not part of the
actor-system contract), surfacing `ObjectDisposedException` instead of the assertion result. Fixed in `96feb62a`:
narrow `ObjectDisposedException` boundary in `ActorInstance.NotifyStoppedAsync` + file-backed SQLite per scope in that test +
explicit runtime drain. Stress: Tenancy.Integration net10.0 25/25 green; net8/net10 157/157; Actor/Tenancy/Core/Flow integration green;
Release build 0W/0E. New CI run on `96feb62a`.

## Pending-work completion round (2026-10-21)

The four issues created from the owner's second feedback round were implemented after the first completion claim:

| Issue | Implementation | Verification |
|---|---|---|
| #375 package fold | `06d7f3a9` — Flow/Scheduling Repository adapters into Foundations, 90→88 packages, slnx/refs/tests/docs/AGENTS updated | Release build 0W/0E; Flow 235+172, Scheduling 165+31 |
| #376 Core tenancy intrusion | `c4ba6f18` — auth-scope routing types to Tenancy, tenancy feature installs facade; Core returns to native registration and keeps only the generic state-pipeline seam | Core/Tenancy unit+integration suites green |
| #378 AppDomainTypeCache | `6fa8b7df` — incremental assembly refresh, GetAllTypes/GetFields/GetProperty, raw reflection routed, D10 documented | new AppDomainTypeCacheShould; build/tests green |
| #379 nullable standard | `4ed3f1d2` — forced-access `!` = 0 in src; null!/default! reduced and AGENTS rule; D9 review-rule guard | global scan; CI green on that commit (analysis/docs) |
| follow-up | `96feb62a` Actor terminal-notification disposal fix + TenantActor test stabilization; `757e8723` inline reasons for every remaining null!/default! declaration, AGENTS path refresh, Core seam wording | Release build 0W/0E; GitHub/Gitea master = `757e8723`; final CI pending |

Gitea state: open 46 / closed 327; #373/#374/#375/#376/#377/#378/#379 all closed with commit evidence.

## CI stabilization (2026-10-21)

- `757e8723` (docs/contract comments; CI 3/3 green on GitHub) annotates every remaining `null!`/`default!` with its runtime contract and refreshes AGENTS runtime paths.
- `96feb62a` fixed the Actor terminal-notification/provider-disposal race and made the tenant actor restart test use a file-backed SQLite database (stress 25/25 on net10.0).
- `33ee3616` widens the Scheduling lost-lease test from 100 ms to 1 s (stress 10/10) after the macOS lane failed the 100 ms takeover/renewal race.
Final verification run: GitHub build/analysis/docs on `33ee3616`.

## Audit after owner question (2026-10-21)

Audited every "待实施" marker: #375/#376/#378/#379 are implemented, CI-green and closed; the ledger line still said "新建、待实施" (stale) and plan Task 16 still demanded the absolute absence of `src/Schemata.Core/State` even though spec X-32 records the deliberate generic state-pipeline seam. Fixed both acceptance texts, removed the last tenant vocabulary from Core comments, and added `AuthenticationFeatureShould` proving Core installs only the native `IAuthenticationService` registration. Commit `89fc7104`, CI on GitHub; gitea SSH was flaky at push time (retrying).
