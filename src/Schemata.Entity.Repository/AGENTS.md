# Schemata.Entity.Repository

Repository core with an advisor pipeline. Providers (`Schemata.Entity.EntityFrameworkCore`, `Schemata.Entity.LinqToDB`) implement persistence; advisors implement orthogonal cross-cutting behavior.

## Layout

```
Advisors/    Built-in advisor implementations + advisor interfaces + suppressed-marker types
Extensions/  ServiceCollection / SchemataBuilder repository activation
```

## Advisor Interface Set

| Interface | Phase |
|---|---|
| `IRepositoryAddAdvisor` | before `Add` |
| `IRepositoryUpdateAdvisor` | before `Update` |
| `IRepositoryRemoveAdvisor` | before `Remove` |
| `IRepositoryQueryAdvisor` | wraps a single query execution |
| `IRepositoryBuildQueryAdvisor` | mutates the IQueryable before execution |
| `IRepositoryResultAdvisor` | post-execution materialization |
| `IRepositoryCommittedAdvisor` | post-commit, fire-and-forget |

## Built-in Advisors

- `AdviceAddCanonicalName`, `AdviceAddConcurrency`, `AdviceAddSoftDelete`, `AdviceAddTimestamp`, `AdviceAddUniqueness`, `AdviceAddValidation` - pre-add population/validation
- `AdviceUpdateTimestamp`, `AdviceUpdateValidation` - pre-update population/validation
- `AdviceRemoveSoftDelete` - rewrites `Remove` into a soft-delete update when the entity implements `ISoftDelete`
- `AdviceBuildQuerySoftDelete` - filters tombstones from queries

## Suppression Markers

`*Suppressed` types (`SoftDeleteSuppressed`, `QuerySoftDeleteSuppressed`, `TimestampSuppressed`, `UniquenessSuppressed`, `UpdateValidationSuppressed`, `AddValidationSuppressed`) opt a single call out of the matching advisor. Use them sparingly and locally - they are scoped via DI markers, not configuration.

## Rules

- Soft-delete and query-soft-delete advisors only run when the entity implements `Schemata.Abstractions.Entities.ISoftDelete`. Marker check happens at registration time; non-soft entities skip both advisors.
- `IRepositoryCommittedAdvisor` runs after commit. A rollback does not undo a committed advisor's side effects - downstream caches may stay live until their TTL expires.
- Pre-commit advisors may abort by throwing. Post-commit advisors must log and swallow; never propagate.
- Add or update advisors must be idempotent across retries - the unit-of-work may replay them when the transaction is rolled back and reissued.
- Do not register the built-in soft-delete advisors when none of the registered entities implement `ISoftDelete`.
