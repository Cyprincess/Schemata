# Mutation Pipeline

Every `AddAsync`, `UpdateAsync`, and `RemoveAsync` on `IRepository<TEntity>` runs an advisor pipeline
before — and sometimes instead of — the backing-store operation. Advisors are sorted by their `Order`
property and run in sequence. Each returns an `AdviseResult`:

- **Continue** — proceed to the next advisor, then to the store operation.
- **Block** — stop the pipeline; skip the store operation.
- **Handle** — stop the pipeline; the advisor has performed an alternative action in place of the store
  operation.

`Block` or `Handle` skips the remaining advisors and the backing-store call.

Each write method returns `Task<MutationResult>`: `Applied` when the call staged or executed a
transactional write, `NoWrite` when an advisor blocked or handled the call without staging a write.
The result describes this call's actual write: a soft delete handles the remove but stages an
update on the same repository, and that nested write's result propagates, so a soft-deleting
remove returns `Applied` while a purely handled no-write call returns `NoWrite`.

## Where the code lives

| Item                                         | Path                                                                            |
| -------------------------------------------- | ------------------------------------------------------------------------------- |
| `IRepositoryAddAdvisor<TEntity>`             | `src/Schemata.Entity.Repository/Advisors/IRepositoryAddAdvisor.cs`              |
| `IRepositoryUpdateAdvisor<TEntity>`          | `src/Schemata.Entity.Repository/Advisors/IRepositoryUpdateAdvisor.cs`           |
| `IRepositoryRemoveAdvisor<TEntity>`          | `src/Schemata.Entity.Repository/Advisors/IRepositoryRemoveAdvisor.cs`           |
| `IRepositoryCommittedAdvisor<TEntity>`       | `src/Schemata.Entity.Repository/Advisors/IRepositoryCommittedAdvisor.cs`        |
| `MutationResult`                             | `src/Schemata.Entity.Repository/MutationResult.cs`                              |
| `IResourceMutation<TEntity>`                 | `src/Schemata.Entity.Repository/IResourceMutation.cs`                           |
| `ResourceMutation<TEntity>`                  | `src/Schemata.Entity.Repository/ResourceMutation.cs`                            |
| `IResourceMutationCommittedAdvisor<TEntity>` | `src/Schemata.Entity.Repository/Advisors/IResourceMutationCommittedAdvisor.cs`  |
| Built-in advisors                            | `src/Schemata.Entity.Repository/Advisors/Advice{Add,Update,Remove}*.cs`         |
| Registration                                 | `src/Schemata.Entity.Repository/Extensions/ServiceCollectionExtensions.cs`      |

## Advisor interfaces

The add, update, and remove advisor interfaces receive the repository and entity alongside the shared
`AdviceContext`:

```csharp
public interface IRepositoryAddAdvisor<TEntity>
    : IAdvisor<IRepository<TEntity>, TEntity> where TEntity : class;

public interface IRepositoryUpdateAdvisor<TEntity>
    : IAdvisor<IRepository<TEntity>, TEntity> where TEntity : class;

public interface IRepositoryRemoveAdvisor<TEntity>
    : IAdvisor<IRepository<TEntity>, TEntity> where TEntity : class;
```

Committed advisors run after persistence succeeds. The repository-level contract is a type-level
notification:

```csharp
public interface IRepositoryCommittedAdvisor<TEntity>
    : IAdvisor<IRepository<TEntity>> where TEntity : class;
```

Entity-level post-commit behavior lives on the resource-mutation path instead:

```csharp
public interface IResourceMutationCommittedAdvisor<TEntity> : IAdvisor
    where TEntity : class
{
    Func<CancellationToken, Task>? Prepare(TEntity entity, Operations operation);
}
```

## Add pipeline

Built-in add advisors, in execution order:

| Order       | Advisor                                             | Trait            | Behavior                                                                                                                                                                    |
| ----------- | --------------------------------------------------- | ---------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 100,000,000 | `AdviceAddTimestamp<TEntity>`                       | `ITimestamp`     | Sets `CreateTime` and `UpdateTime` to the current UTC time. Suppressed by `TimestampSuppressed`.                                                                            |
| 110,000,000 | `AdviceAddConcurrency<TEntity>`                     | `IConcurrency`   | Mints a new GUID for `Timestamp`.                                                                                                                                           |
| 120,000,000 | `AdviceAddCanonicalName<TEntity>`                   | `ICanonicalName` | Resolves the `[CanonicalName]` pattern and writes `CanonicalName`. No suppress flag.                                                                                        |
| 121,000,000 | `AdviceAddOwner<TEntity>`                           | `IOwnable`       | Calls `IOwnerResolver<TEntity>.ResolveAsync` and sets `Owner`. Registered by `UseOwner()`. Suppressed by `OwnerSuppressed`.                                                 |
| 130,000,000 | `AdviceAddValidation<TEntity>`                      | (any)            | Runs `IValidationAdvisor<TEntity>` for `Operations.Create`. Throws `ValidationException` when an advisor blocks. Suppressed by `AddValidationSuppressed`.                   |
| 140,000,000 | `AdviceValidateResourceReferences<TEntity>`         | (any)            | Resolves every `[ResourceReference]` value through `IResourceTypeResolver`: a typed reference resolving to the wrong type throws `NotFoundException`; an unresolvable polymorphic reference raises `ValidationException` (`INVALID_REFERENCE`). Skips when no resolver is registered.                                     |
| 150,000,000 | `AdviceValidateResourceReferenceExistence<TEntity>` | (any) | Verifies opted-in reference targets. Registered by `AddRepository` for add/update; missing targets produce `NotFoundException`, missing required resolver produces `InvalidOperationException`. |
| 160,000,000 | `AdviceAddUniqueness<TEntity>`                      | (any)            | Looks up the entity by key (with the query soft-delete filter suppressed); throws `AlreadyExistsException` when a row already exists. Suppressed by `UniquenessSuppressed`. |
| 900,000,000 | `AdviceAddSoftDelete<TEntity>`                      | `ISoftDelete`    | Clears `DeleteTime` to `null`. Suppressed by `SoftDeleteSuppressed`.                                                                                                        |

After every advisor returns `Continue`, the entity is staged for the store: EF Core calls
`Context.AddAsync(entity)`; LinqToDB inserts immediately inside the active transaction.

`AdviceAddOwner` activates through `UseOwner()`. Logical existence validation activates per
property through `ValidateExistence`, independently of ownership. These pre-write checks do not
provide database-level referential integrity or prevent a concurrent target deletion.

### Consumer-owned resource names

The consuming application supplies each resource's `Name`, either explicitly before creation or
through its own `IRepositoryAddAdvisor<TEntity>`. Framework producers leave missing resource names
for that advisor; the framework provides no fallback name generator. This applies to persisted
framework resources as well as application entities, including authorization tokens and grants,
security material, subject mappings, Flow runtime records, jobs, and report records. BPMN graph node
and graph reference names are internal graph identifiers; persisted Flow runtime resources are not
exempt.

Run the naming advisor before `AdviceAddCanonicalName.DefaultOrder` (120,000,000). The canonical
advisor derives `CanonicalName` from the resource pattern and the supplied `Name`; a missing, empty,
or whitespace-only required segment raises `ValidationException`. Setting `CanonicalName` alone
does not bypass resolution. `AdviceAddIdentifier` generates a persistence `Uid`, not a resource
`Name`.

This consumer-defined policy assigns a GUID segment only when `Name` is absent and preserves an
explicit name. The open-generic registration covers every repository entity implementing
`ICanonicalName`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

var services = new ServiceCollection();
services.TryAddEnumerable(ServiceDescriptor.Scoped(
    typeof(IRepositoryAddAdvisor<>),
    typeof(ResourceNameAdvisor<>)));

public sealed class ResourceNameAdvisor<TEntity> : IRepositoryAddAdvisor<TEntity>
    where TEntity : class
{
    public int Order => AdviceAddCanonicalName.DefaultOrder - 1;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext context,
        IRepository<TEntity> repository,
        TEntity entity,
        CancellationToken ct)
    {
        if (entity is ICanonicalName named && string.IsNullOrWhiteSpace(named.Name)) {
            named.Name = Guid.NewGuid().ToString("N");
        }

        return Task.FromResult(AdviseResult.Continue);
    }
}
```

Use the host's service collection in an application. To restrict this policy to token resources,
replace the open-generic registration above with this closed registration, using the same advisor
class:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Entity.Repository.Advisors;
using Schemata.Security.Skeleton.Entities;

services.TryAddEnumerable(
    ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataToken>,
        ResourceNameAdvisor<SchemataToken>>());
```

Register corresponding closed advisors for every other resource the host creates, or use one
open-generic policy. Naming must complete before downstream code reads the created resource's
`Name` or `CanonicalName` to form references.

The execution paths are
`src/Schemata.Entity.Repository/Advisors/AdviceAddCanonicalName.cs`,
`src/Schemata.Common/ResourceNameDescriptor.cs` (`Resolve`), and the enumerable registrations in
`src/Schemata.Entity.Repository/Extensions/ServiceCollectionExtensions.cs`.

## Update pipeline

| Order       | Advisor                                             | Trait        | Behavior                                                                                                                                                     |
| ----------- | --------------------------------------------------- | ------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| 100,000,000 | `AdviceUpdateTimestamp<TEntity>`                    | `ITimestamp` | Sets `UpdateTime` to the current UTC time. Suppressed by `TimestampSuppressed`.                                                                              |
| 110,000,000 | `AdviceUpdateValidation<TEntity>`                   | (any)        | Runs `IValidationAdvisor<TEntity>` for `Operations.Update`. Throws `ValidationException` when an advisor blocks. Suppressed by `UpdateValidationSuppressed`. |
| 140,000,000 | `AdviceValidateResourceReferences<TEntity>`         | (any)        | Resolves every `[ResourceReference]` value through `IResourceTypeResolver`, as on add.                                                                       |
| 150,000,000 | `AdviceValidateResourceReferenceExistence<TEntity>` | (any) | Verifies opted-in logical references, as on add; registered by `AddRepository`. |

There is no update-side concurrency advisor. Optimistic concurrency on update is enforced by the
database when the concrete entity annotates `IConcurrency.Timestamp` with `[ConcurrencyCheck]`.
The caller's stamp is the original value guarding the write; EF Core keeps the same tracked
instance (no detach/re-attach) and the unit of work mints the next stamp immediately before
`SaveChangesAsync`, which issues a guarded `UPDATE ... WHERE Timestamp = @original`; a zero-row
result becomes `AbortedException`. When the save fails, the caller-visible stamp is restored before
rollback sinks run. LinqToDB's `UpdateOptimisticWithRefreshAsync` rotates the stamp inside the
guarded update statement itself. See
[providers.md](providers.md) and [entity/traits.md](../entity/traits.md#iconcurrency).

## Remove pipeline

| Order       | Advisor                           | Trait         | Behavior                                                                                                                                                                      |
| ----------- | --------------------------------- | ------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 900,000,000 | `AdviceRemoveSoftDelete<TEntity>` | `ISoftDelete` | Sets `DeleteTime` to the current UTC time, calls `repository.UpdateAsync(entity)`, and returns `Handle` to prevent the physical delete. Suppressed by `SoftDeleteSuppressed`. |

When `AdviceRemoveSoftDelete` returns `Handle`, the nested update's result propagates, so the
remove call returns `MutationResult.Applied`; the row stays with a non-null `DeleteTime`, and
later queries exclude it via `AdviceBuildQuerySoftDelete`. When the entity does not implement
`ISoftDelete`, or `SoftDeleteSuppressed` is active, the entity is physically removed and the call
returns `Applied`.

## Resource mutation owner

`IResourceMutation<TEntity>` is the common boundary for resource-semantic writes (AIP Create,
Update, Delete and their variants). The default implementation, `ResourceMutation<TEntity>`, is
registered by `AddRepository` as an open generic with `TryAddScoped`; a domain owner replaces it
with a closed registration and extends staging through the protected `Stage*Async` seams — the
public commit orchestration is never overridden.

```csharp
public interface IResourceMutation<TEntity> where TEntity : class
{
    Task<MutationResult> CreateAsync(TEntity entity, IUnitOfWork? transaction = null, CancellationToken ct = default);
    Task<MutationResult> UpdateAsync(TEntity entity, IUnitOfWork? transaction = null, Operations operation = Operations.Update, CancellationToken ct = default);
    Task<MutationResult> DeleteAsync(TEntity entity, IUnitOfWork? transaction = null, Operations operation = Operations.Delete, CancellationToken ct = default);
}
```

Transaction ownership is explicit:

- Without a `transaction` argument the operation owns its transaction: it resolves a fresh
  repository, begins a unit of work, stages, commits, and disposes both. Two consecutive calls are
  two independent transactions.
- With a `transaction` argument the mutation joins that unit of work and only stages; the outer
  owner commits or rolls back. Nothing is visible to other connections before the outer commit.

`DeleteAsync` with `Operations.Expunge` or `Operations.Purge` holds the soft-delete suppression
around staging, so the remove is physical. A blocked or handled stage returns `NoWrite` and commits
nothing new; a throwing stage or a failing commit propagates and rolls the transaction back.

Purge and Identity claim removal begin their outer unit of work before the first query and
materialize at most 100 matching rows per keyset page before writing. Purge advances by canonical
name; claim removal advances by claim `Uid`. Every page uses the same transaction, preserving
query advisors and per-row mutation advisors while closing its reader before staging writes.
The LINQ to DB schema metadata reader maps `Guid.CompareTo(Guid)` to a server-side comparison
over native Guid values, matching the database's `OrderBy(Uid)` order for claim-page continuation.
This mapping belongs to each configured schema in
`src/Schemata.Entity.LinqToDB/SystemComponentModelDataAnnotationsSchemaAttributeReader.cs`.
The operation commits once after all pages; cancellation or a mutation failure rolls back the
whole operation. Purge conveys `Operations.Purge` to the resource mutation, which holds physical
delete suppression on its own joined repository. Preview only counts and samples matching rows.
Each Identity claim-removal call resolves a fresh repository from the store's injected scoped
`IServiceProvider`; that repository and its explicit transaction belong to the call. The store's
query repositories remain available for subsequent reads and removals.
The execution paths are `src/Schemata.Resource.Foundation/PurgeJob.cs` and the claim-removal methods
in `src/Schemata.Identity.Skeleton/Stores/SchemataUserStore.cs` and `SchemataRoleStore.cs`.

## Committed pipeline

Two post-commit extension points exist, ordered by `CommitOrders` segments:

1. **Repository segment** — after a commit with at least one staged write, the repository runs its
   `IRepositoryCommittedAdvisor<TEntity>` chain once. The notification is type-level: the cache
   package's `AdviceCommittedEvictCache<TEntity>` publishes a new entity-type cache generation and
   honors `QueryCacheEvictionSuppressed`. A commit with no writes sends no notification.
2. **Resource segment** — on the `IResourceMutation<TEntity>` path, each
   `IResourceMutationCommittedAdvisor<TEntity>` prepares a callback per staged mutation; the
   callbacks run after the transaction commits. Pending-event flushing uses this segment.
   `Prepare` must be side-effect free: it captures the operation's data and returns the callback,
   or `null` when nothing must run.

Committed notifications never run when persistence fails or the unit of work rolls back. When a
commit sink throws, the remaining sinks still run and the errors are rethrown afterwards; the
committed data stays durable.

## Registration

`AddRepository` registers all built-in mutation advisors as open generics:

```csharp
services.AddRepository<Book, EfCoreRepository<AppDbContext, Book>>();
```

The container closes each open generic at resolve time, so `IRepositoryAddAdvisor<Book>`
materializes `AdviceAddTimestamp<Book>`, `AdviceAddConcurrency<Book>`, and the rest. Add a custom
advisor with `TryAddEnumerable`:

```csharp
services.TryAddEnumerable(ServiceDescriptor.Scoped(
    typeof(IRepositoryUpdateAdvisor<>),
    typeof(MyAuditAdvisor<>)));
```

Choose `Order` according to the invariant's prerequisites. A naming advisor must precede canonical
resolution; unrelated extensions can use positions outside the built-in
`[100_000_000, 900_000_000]` window.

## Extension points

- **New mutation behavior** — implement the relevant advisor interface, check `entity is IMyTrait`,
  and return `Continue` when the trait is absent.
- **Post-commit type notification** — implement `IRepositoryCommittedAdvisor<TEntity>` when the
  extension reacts to any committed write of the entity type.
- **Post-commit entity behavior** — implement `IResourceMutationCommittedAdvisor<TEntity>` when the
  extension needs the mutated entity and operation on the resource-mutation path.
- **Suppression** — add a `sealed class MySuppressed;` marker, check `ctx.Has<MySuppressed>()` at
  the top of `AdviseAsync`, and expose a `SuppressMy()` extension that calls
  `AdviceContext.Use<MySuppressed>()`.

## See also

- [query-pipeline.md](query-pipeline.md) — build-query/query/result advisor chains
- [unit-of-work.md](unit-of-work.md) — enlistment, ordered commit sinks, and stamp timing
- [entity/traits.md](../entity/traits.md) — trait interfaces and advisor order numbers
