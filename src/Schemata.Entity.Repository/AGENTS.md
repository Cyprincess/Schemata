# Schemata.Entity.Repository

## OVERVIEW

Generic `IRepository<TEntity>` / `IUnitOfWork` abstraction with an advisor-driven query/mutation pipeline. Highest fan-in after the kernel (~14 dependents). Deps: `Schemata.Advice`, `Schemata.Common`, `Schemata.Validation.Skeleton`. No provider reference.

Dependents: Identity.Skeleton, Authorization.Skeleton, Resource.Foundation, Flow.Skeleton, Scheduling.Skeleton, Tenancy.Skeleton, Push.Skeleton. Sibling providers/add-ons (domain map: src/AGENTS.md) all depend here; none depend on each other.

## STRUCTURE

- `IRepository.cs`, `IUnitOfWork.cs`, `IUnitOfWorkSink.cs`: contracts.
- `RepositoryBase.cs` (769 lines): abstract base. Advisor-dispatched query/mutation surface, change tracking, UoW enlistment, suppression scopes.
- `Extensions/ServiceCollectionExtensions.cs`: `AddRepository(Type)` / `AddRepository<TEntity, TImpl>()`, registers built-in advisors via `RegisterAdvisors`, returns `SchemataRepositoryBuilder`.
- `Advisors/`: phase interfaces and built-in `Advice*` implementations.
- `CommitChanges.cs`, `QueryContext.cs`, `QueryContainer.cs`, `Conversions/JsonValueConverter.cs`.

## ENTRY POINTS

- DI: `services.AddRepository(typeof(MyEntity))` or `services.AddRepository<MyEntity, MyRepository>()`.
- Per-repo lifecycle: implement `IRepository<TEntity>`, typically by deriving from `RepositoryBase<TEntity, TKey>`.
- Builder verbs (sibling packages):
  - EF Core: `.UseEntityFrameworkCore<TContext>(...)`, `.WithUnitOfWork<TContext>()`.
  - LinqToDB: `.UseLinqToDb(...)`.
  - Cache: `.UseQueryCache(...)`.
  - Owner: `.UseOwner()`.

## ADVISOR PHASES

Pipeline resolution semantics: root AGENTS.md.

- `IRepositoryBuildQueryAdvisor`: pre-query predicate/filter assembly (owner scoping).
- `IRepositoryAddAdvisor`: pre-insert validation, ownership stamping, soft-delete default.
- `IRepositoryUpdateAdvisor`: pre-update hooks. `Handle` here silently skips `RepositoryBase.Update`.
- `IRepositoryRemoveAdvisor`: pre-delete; soft-delete marks removed instead of dropping.
- `IRepositoryQueryAdvisor`: query interception (caching).
- `IRepositoryResultAdvisor`: post-fetch shaping (cache hydration).
- `IRepositoryCommittedAdvisor`: runs after successful commit only. No rollback path.

Built-in advisors: canonical-name (`AdviceCanonicalName`), concurrency token, timestamps, uniqueness, validation, soft-delete (add/remove/query), resource-reference validation (`AdviceValidateResourceReferences`; write-time rule: root AGENTS.md), suppression marker types.

## GOTCHAS

- Advisor pipeline runs only through `IRepository<T>` methods. Mutating `DbContext`/`DataConnection` directly bypasses everything.
- `Handle` from an update advisor silently skips the repository's `Update`.
- Repositories don't share a context by default. Enlist each repository to share one transaction.
- `RepositoryBase.Dispose` disposes only its implicit UoW. An externally-enlisted UoW is the caller's to dispose.
- Provider gotchas (EF Core / LinqToDB packages; recorded here for repository consumers):
  - EF Core: `Detach` is required whenever the change tracker already saw the same row; tracker wins over your explicit update.
  - LinqToDB: schema reader forces TEXT for non-primitive props with JSON converter; `CreateTable<T>` silently skips collections otherwise.

Canonical docs: `docs/documents/repository/**` (mutation-pipeline.md, providers.md, caching.md, unit-of-work.md).