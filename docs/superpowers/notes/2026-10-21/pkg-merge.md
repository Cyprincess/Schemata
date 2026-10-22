# pkg-merge report — #375 (plan Task 15)

Scope: fold `Schemata.Flow.Repository` and `Schemata.Scheduling.Repository` into their Foundations.
No `dotnet build`/`dotnet test` was run (per IMPL-BRIEF hard rule 2); all verification was static.

## Moved source files (4, added under Foundation/Repository/, content identical except the one namespace/using line each)

| Old path | New path | Change |
|---|---|---|
| `src/Schemata.Flow.Repository/RepositoryFlowEffectRecorder.cs` | `src/Schemata.Flow.Foundation/Repository/RepositoryFlowEffectRecorder.cs` | `namespace Schemata.Flow.Repository` → `Schemata.Flow.Foundation` |
| `src/Schemata.Flow.Repository/Extensions/SchemataFlowRepositoryServiceCollectionExtensions.cs` | `src/Schemata.Flow.Foundation/Repository/SchemataFlowRepositoryServiceCollectionExtensions.cs` | `using Schemata.Flow.Repository;` → `using Schemata.Flow.Foundation;` (DI class stays in `Microsoft.Extensions.DependencyInjection`) |
| `src/Schemata.Scheduling.Repository/RepositoryJobExecutionStore.cs` | `src/Schemata.Scheduling.Foundation/Repository/RepositoryJobExecutionStore.cs` | `namespace Schemata.Scheduling.Repository` → `Schemata.Scheduling.Foundation` |
| `src/Schemata.Scheduling.Repository/Extensions/SchemataSchedulingRepositoryServiceCollectionExtensions.cs` | `src/Schemata.Scheduling.Foundation/Repository/SchemataSchedulingRepositoryServiceCollectionExtensions.cs` | `using Schemata.Scheduling.Repository;` → `using Schemata.Scheduling.Foundation;` (DI class stays in `Microsoft.Extensions.DependencyInjection`) |

`diff` against `HEAD:` shows exactly one changed line per file. No behavior edits, no API shape changes other than
the intended namespace move. BOM preserved on the two files that had one. `Schemata.Flow.Foundation` no longer
references the deleted project; `Schemata.Scheduling.Foundation` already referenced every dependency the moved
store needs (`Entity.Repository`, `Scheduling.Skeleton`, `Messaging.Skeleton`, `Core`→`Abstractions`), so no new
reference was added there.

## Deleted projects

- `src/Schemata.Flow.Repository/` — `Schemata.Flow.Repository.csproj`, `FodyWeavers.xml` (tracked),
  moved code files; generated `FodyWeavers.xsd` (gitignored) removed with the directory.
- `src/Schemata.Scheduling.Repository/` — same shape.
- `Schemata.slnx`: removed both `<Project Path=...>` entries (165 → 163 projects).

## Modified files (24)

- `Schemata.slnx` (2 lines removed), `AGENTS.md` (2 package-count edits).
- `src/Schemata.Flow.Foundation/Schemata.Flow.Foundation.csproj` — removed `ProjectReference` to deleted package.
- 9 test csproj — removed `ProjectReference` to deleted packages (10 lines total):
  `tests/Schemata.Actor.Scheduling.Tests`, `tests/Schemata.Flow.Integration.Tests` (2),
  `tests/Schemata.Flow.Tests`, `tests/Schemata.Report.Integration.Tests`,
  `tests/Schemata.Resource.Grpc.Integration.Tests`, `tests/Schemata.Resource.Http.Integration.Tests`,
  `tests/Schemata.Scheduling.Integration.Tests`, `tests/Schemata.Scheduling.Tests`,
  `tests/Schemata.Tenancy.Integration.Tests`.
- 12 test source files — removed the now-nonexistent namespace import or retargeted the global alias:
  `tests/Schemata.Flow.Tests/GlobalUsings.cs` (alias now `Schemata.Flow.Foundation.RepositoryFlowEffectRecorder<`);
  `tests/Schemata.Flow.Tests/RepositoryFlowEffectRecorderShould.cs`;
  `tests/Schemata.Flow.Integration.Tests/{FlowEffectRecoveryShould,Fixtures/EfCoreFlowFixture,Fixtures/FlowFixtureServices,Fixtures/LinqToDbFlowFixture,Fixtures/TestDbContext,Resource/FlowCustomMethodGrpcShould,Resource/Program}.cs`;
  `tests/Schemata.Tenancy.Integration.Tests/{TenantJobShould,TenantReminderShould,TenantSchedulingShould}.cs`
  (the latter already import `Schemata.Scheduling.Foundation`, so `RepositoryJobExecutionStore<,>` resolves there).

## Docs

- `AGENTS.md`: `90` → `88` in both places (overview sentence + `src/` key-directory row).
- `docs/documents/packages.md`: **no edit required** — at HEAD it already says "88 source package projects"
  (verified correct after the merge), and it contains no list/table entry for either deleted package.
- `docs/superpowers` plan/spec counts left untouched (per task instruction; only this report file was written there).

## Verification performed (static only)

- `git grep` across tracked files: zero remaining references to either package outside `docs/documents/scheduling/*`
  and `docs/guides/scheduling.md` (see residual risk below).
- XML parses cleanly for every edited `.csproj` and `Schemata.slnx`.
- Script-checked every `ProjectReference` path in `src/`, `tests/`, `targets/`, `generators/`: 0 unresolved.
- `find src -maxdepth 2 -name '*.csproj'` = 88; slnx project count = 163.
- No duplicate `RepositoryFlowEffectRecorder` / `RepositoryJobExecutionStore` / extension-method definitions.
- `src/` scan: no XML-doc or comment mentions the old package names, so no XML-doc edits were needed.

## Risks / not done

1. **Compile not verified** (build forbidden). Main residual compile risk: `tests/Schemata.Flow.Integration.Tests`
   and `tests/Schemata.Report.Integration.Tests` call `AddSchemataSchedulingRepositoryStore()` but reference
   `Schemata.Scheduling.Foundation` only transitively (via `Scheduling.Http`/`Scheduling.Grpc`/`Report.Scheduling`).
   Transitive project references are enabled in this repo, so this should compile; if the build disagrees, add an
   explicit `ProjectReference` to `Schemata.Scheduling.Foundation` in those two test projects.
2. **Stale docs outside assigned scope** (6 refs, `git grep`): `docs/guides/scheduling.md:13`,
   `docs/documents/scheduling/overview.md:11,26,184`, `docs/documents/scheduling/jobs.md:47`,
   `docs/documents/scheduling/persistence.md:346` still tell readers to install `Schemata.Scheduling.Repository` or
   point at its old path. They need a follow-up docs edit (plan Task 13 Step 6 owns the doc-reference sweep).
3. Intended breaking surface: the two `*.Repository` packages no longer exist; their public types now live in
   `Schemata.Flow.Foundation` / `Schemata.Scheduling.Foundation` (new namespaces). Consumers referencing the old
   packages must retarget — this is the point of #375 / the `refactor!` commit.
4. `docs/documents/packages.md` said 88 already at HEAD (stale before this change, accurate after). Worth noting in
   review so it is not mistaken for an un-edited requirement.

## Files changed: 24 modified + 4 added (moved) + 6 tracked deleted (2 csproj, 2 FodyWeavers.xml, 2 moved sources;
plus gitignored xsds removed with the directories).
