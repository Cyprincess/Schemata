# Resource: single commit owner for delete and undelete (plan Task 4 / X-05)

Task: `task-10`; plan `docs/superpowers/plans/2026-10-20-post-review-remediation-plan.md` Task 4; spec X-05 (`2026-10-20-post-review-remediation-spec.md`); issues #360, #287, #318.
Workspace HEAD at implementation time: `c44090aa` (plus other agents' uncommitted work in unrelated files — not touched).

## Files changed

Production (`src/Schemata.Resource.Foundation`):

- **new** `ResourceCommitOwner.cs` — the one commit owner for a standard-method dispatch:
  `ExecuteAsync<TEntity,TResult>(IRepository<TEntity>, ResourceDispatchExecution?, Func<IUnitOfWork,CancellationToken,Task<TResult>>, CancellationToken)`
  begins the unit of work, runs every fail-able step inside the mutate delegate, commits once
  (`ResourceCommitOwner.cs:45-73`), plus the shared `SyncStamp` save-preparation
  (`ResourceCommitOwner.cs:85-97`, moved from `ResourceOperationHandler`).
- `ResourceOperationHandler.cs` — private `ExecuteOwnedAsync<TResponse>(mutate, execution, ct)`
  is now the plan's signature (`ResourceOperationHandler.cs:139-147`) and delegates to
  `ResourceCommitOwner`; the previous inline transaction body and `SyncStamp` were removed so
  there is exactly one implementation.
- `ResourceOperationHandler.Create.cs` / `ResourceOperationHandler.Update.cs` — call the new
  delegate shape (mutate now receives the transaction and cancellation token) and use
  `ResourceCommitOwner.SyncStamp`.
- `ResourceOperationHandler.Delete.cs` — `DeleteAsync` now runs through `ExecuteOwnedAsync`
  (`:114-117`); the mutation and the tombstone detail mapping (soft-delete `ISoftDelete { DeleteTime: not null }`
  path) moved into `MutateDeleteAsync` (`:120-149`) **before** the commit, with `SyncStamp` so the
  pre-commit-mapped ETag still matches the persisted row.
- `UndeleteHandler.cs` — `HandleAsync` runs the restore through `ResourceCommitOwner.ExecuteAsync`
  (`:64-69`); `MutateUndeleteAsync` (`:71-91`) stages `UpdateAsync(..., transaction, Operations.Undelete, ...)`,
  maps the restored detail and throws its existing `InvalidOperationException` for a null map
  **before** commit, with `SyncStamp`.
- `Handlers/ExpungeHandler.cs` — same owner for the AIP-164 expunge standard method
  (`:56-61`, `MutateExpungeAsync` `:63-73`). Deviates from the plan's file list (it names only
  Delete/Undelete): the acceptance says *all* standard/custom methods have one commit owner, and
  expunge was the only remaining standard handler committing inside `ResourceMutation`. Its
  response is a constant so no fail-able projection exists, but leaving it self-committing would
  keep a second owner.

`src/Schemata.Entity.Repository/ResourceMutation.cs` — **intentionally unchanged**. When a unit of
work is supplied the method already only joins/stages and never commits
(`ResourceMutation.cs:119-128`), so there is no redundant commit to delete. The
`transaction == null` self-commit branch (`:132-141`) is kept because real callers outside this
task's scope pass `null` and rely on it (`Schemata.Authorization.Foundation` managers,
`Identity.Scim` group adapter, `PairwiseSubjectTranslator`, …); the task explicitly says to keep
the `IUnitOfWork?` parameter for real callers and those files are outside my edit scope. From
`src/Schemata.Resource.Foundation` every mutation now receives the handler's outer UoW
(`PurgeJob.cs:103` already did).

Tests:

- **new** `tests/Schemata.Resource.Tests/ResourceOperationHandlerDeleteShould.cs`
  - `Delete_WhenTombstoneMappingThrows_DoesNotCommitTheRemoval` — mapper throws for the tombstone
    detail; asserts the mapper ran before `CommitAsync`, the mutation received the handler's UoW,
    and the UoW never committed.
  - `Delete_HardDelete_CommitsWithoutMappingADetail` — strict mapper (any call would throw) proves
    a hard delete commits without a detail projection.
- `tests/Schemata.Resource.Tests/ResourceMethodHandler/UndeleteHandlerShould.cs`
  - new `Invoke_WhenDetailMappingThrows_DoesNotCommitTheRestore` (same before-commit ordering +
    no commit assertions); success/no-write tests updated to the handler-owned UoW contract.
- `tests/Schemata.Resource.Tests/ResourceMutationNoWriteShould.cs` — the two delete tests now
  assert the mutation receives the UoW, the detail is mapped after the mutation but before the
  commit, and the UoW commits once.
- `tests/Schemata.Resource.Tests/Features/SchemataResourceFeatureRegisterMethodShould.cs`,
  `ResourceInstallationReplayShould.cs`, `ResourceMethodHandler/ResourceMethodEnvelopeShould.cs` —
  updated fixtures to the new owner (repository `Begin()` returns the mock UoW, delete/expunge
  call receives a non-null UoW, one commit).
- **new** `tests/Schemata.Resource.Http.Integration.Tests/ResourceCommitOwnershipShould.cs` — the
  plan's required **DB-row** assertions over a real SQLite host with a faulting `ISimpleMapper`
  decorator:
  - `Delete_WhenTombstoneMappingFails_LeavesTheRowLive` (HTTP ≥500, row still exists with
    `DeleteTime == null` via `IgnoreSoftDelete: true`);
  - `Undelete_WhenDetailMappingFails_LeavesTheRowDeleted` (HTTP ≥500, row still has `DeleteTime`).

## Design / decisions

- One implementation: both the generic handler and the two standalone standard-method handlers use
  `ResourceCommitOwner`; `ResourceOperationHandler` only forwards to it, so the plan's interface is
  honoured without copying the transaction body into a second path.
- Response shaping that can fail (mapper, detail construction) is inside the mutate delegate, so a
  failure throws before the commit and the rollback sinks/dispose discard the staged write. The
  same rule that #360 established for Create/Update now covers Delete/Undelete/Expunge.
- The idempotency completion evidence stays on the unit-of-work commit/rollback sinks inside the
  owner; Delete has no `ResourceDispatchExecution` (delete is not AIP-155 identified), so it passes
  `null` and only the transaction owner is used.
- `SyncStamp` moved to the shared owner because the tombstone/restored detail is now mapped before
  commit; without it the ETag would be stale after provider stamp rotation — behavior is preserved
  relative to the previous after-commit mapping.
- A null tombstone/restored detail remains tolerated for Delete (previous behavior: omit the
  detail), while Undelete keeps its existing `InvalidOperationException` for a null map; only the
  timing (before commit) changed.

## Verification (serialized `/tmp/dsh-build.sh`; Release; `TreatWarningsAsErrors=true`)

- `dotnet build src/Schemata.Resource.Foundation` — succeeded, 0 warnings / 0 errors.
- `Schemata.Resource.Tests` — **319/319 passed** on net10.0 and **319/319** on net8.0.
- `Schemata.Resource.Http.Integration.Tests` — **100/100 passed** on net10.0 and **100/100** on
  net8.0 (was 98 before, +2 new DB-row tests).
- `Schemata.Resource.Grpc.Integration.Tests` — **45/45 passed** on net10.0 and **45/45** on net8.0.
- Plan Step 4 check: `grep -rn "CommitAsync" src/Schemata.Resource.Foundation src/Schemata.Entity.Repository`
  now shows exactly: `ResourceCommitOwner.cs:68` (standard-method owner),
  `PurgeJob.cs:111` (purge job owns its background operation transaction; it passes the UoW to the
  mutation), `RepositoryBase.cs` (`IRepository.CommitAsync` owner), and `ResourceMutation.cs:140`
  (self-commit for out-of-scope `null`-UoW callers). No standard-method handler commits through
  `ResourceMutation` any more.
- Honest limitation: the implementation was written before the tests, so I did not execute the new
  tests against the pre-fix handler and observe red; by construction the new assertions target the
  old behavior directly (old code committed inside `ResourceMutation` and mapped afterwards, so the
  SQLite row would have shown the committed delete/undelete when the mapper threw; the unit tests
  assert "mapper ran before `CommitAsync`" and "no commit"). The workspace is shared and no commit
  was made per the brief, so a clean old-vs-new run was not possible without disturbing other
  agents.

## Incomplete / risky

- Custom-method (AIP-136) action handlers still own their own unit of work inside the domain
  handler by design (one action in one handler; the resource method dispatch deliberately opens no
  competing transaction). No change here; the generalized owner is available to them but wiring
  their business action into the outer resource transaction would be a different design decision.
- `ResourceMutation`'s `null`-transaction self-commit branch remains for out-of-scope callers; if the
  owner wants a single commit owner everywhere, those call sites (Authorization managers, SCIM
  adapter, PairwiseSubjectTranslator, tenant mutations) need a separate task.
- Delete/undelete now `Begin()` a transaction through the injected repository even when the
  mutation is a no-op (e.g. an advisor handled the delete/restore); the commit is empty in that
  case, matching how create/update already behave.
- No public API changed; no commits made.
