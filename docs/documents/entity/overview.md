# Entity Overview

A Schemata entity is a plain C# class or record. Persistence and API behavior attach through trait
interfaces — small interfaces declaring one or more properties. A trait carries no logic; behavior
comes from advisors registered with the repository pipeline, each of which checks for its trait with
an `is`-test or a constrained generic parameter and acts only when the entity matches.
`AddRepository` wires every built-in trait advisor, so implementing a trait is the whole opt-in.

Two opt-in packages extend the entity layer with the same advisor pattern: `Schemata.Entity.Owner`
(`UseOwner()`) wires ownership traits and per-owner query filtering, and `Schemata.Entity.Cache`
(`UseQueryCache()`) wires transparent query caching and committed eviction — see
[query-cache.md](query-cache.md).

## Where the code lives

| Item                         | Path                                                           |
| ---------------------------- | -------------------------------------------------------------- |
| Trait interfaces             | `src/Schemata.Abstractions/Entities/`                          |
| `CanonicalNameAttribute`     | `src/Schemata.Abstractions/Entities/CanonicalNameAttribute.cs` |
| `PrimaryKeyAttribute`        | `src/Schemata.Abstractions/Entities/PrimaryKeyAttribute.cs`    |
| `IndexAttribute`             | `src/Schemata.Abstractions/Entities/IndexAttribute.cs`         |
| Built-in repository advisors | `src/Schemata.Entity.Repository/Advisors/`                     |
| Key resolution               | `src/Schemata.Entity.Repository/RepositoryBase.cs`             |

## How traits activate behavior

Each trait is a contract between the entity and the advisor pipeline. When a repository operation
runs, the matching advisor tests `entity is ITrait` (or `typeof(ITrait).IsAssignableFrom(typeof(TEntity))`).
A passing test runs the advisor's logic; a failing test returns `AdviseResult.Continue` in constant
time without touching the entity. The full trait-to-advisor mapping, with order numbers, lives in
[traits.md](traits.md).

## Defining an entity

An entity with timestamps, soft-delete, and a canonical name:

```csharp
using Schemata.Abstractions.Entities;

[PrimaryKey(nameof(Uid))]
[CanonicalName("publishers/{publisher}/books/{book}")]
public class Book : IIdentifier, ITimestamp, ISoftDelete, ICanonicalName
{
    public Guid      Uid           { get; set; }
    public string?   Name          { get; set; }
    public string?   CanonicalName { get; set; }
    public DateTime? CreateTime    { get; set; }
    public DateTime? UpdateTime    { get; set; }
    public DateTime? DeleteTime    { get; set; }
    public DateTime? PurgeTime     { get; set; }
}
```

Traits need no registration. `services.AddRepository<Book, EfCoreRepository<AppDbContext, Book>>()`
registers the built-in advisors as scoped services, so any entity implementing a trait receives the
matching behavior.

## Primary key convention

`Schemata.Entity.Repository.RepositoryBase.ResolveKeyProperties` resolves keys in two steps:

1. Read the class-level `[PrimaryKey(nameof(A), nameof(B))]` attribute
   (`Schemata.Abstractions.Entities.PrimaryKeyAttribute`). The attribute's `Properties` become the
   key, in declaration order.
2. Fall back to the `IIdentifier.Uid` property when no attribute resolves.

`IIdentifier` uses `Guid` for AIP alignment and to support client-assigned inserts without a database
sequence. `Schemata.Entity.LinqToDB` recognizes the same class-level `[PrimaryKey]` through its
metadata reader, so a single declaration keys both providers. The attribute lives in
`Schemata.Abstractions` rather than any ORM package, so the contract layer stays provider-neutral.
Both providers alias it where a same-named ORM attribute is in scope; in application code
`using Schemata.Abstractions.Entities;` is enough.

## Index declaration

`[Index(nameof(A), nameof(B), IsUnique = true)]`
(`Schemata.Abstractions.Entities.IndexAttribute`) declares a secondary index over one or more
properties. It is class-level and `AllowMultiple`, so repeat it per index:

```csharp
[PrimaryKey(nameof(Uid))]
[Index(nameof(Email), IsUnique = true)]
[Index(nameof(LastName), nameof(FirstName))]
public class Student : IIdentifier { /* ... */ }
```

EF Core emits the declared indexes through `SchemataModelCustomizer`. LINQ to DB parses `[Index]`
but does not emit it (its mapping metadata has no index equivalent); create those indexes through
the application's schema-management path. Both providers honor `[PrimaryKey]` for key mapping.

## Trait composition

Traits are independent and compose freely:

```csharp
public class Order : IIdentifier, ITimestamp, IConcurrency, ISoftDelete,
                     ICanonicalName, IOwnable, IDescriptive
{
    // ...
}
```

The pipeline runs each advisor in `Order` sequence. Advisors that do not apply to the entity return
`Continue` without modifying it.

## Extension points

- **Custom advisor** — implement `IRepositoryAddAdvisor<TEntity>`, `IRepositoryUpdateAdvisor<TEntity>`,
  `IRepositoryRemoveAdvisor<TEntity>`, or `IRepositoryBuildQueryAdvisor<TEntity>` and register with
  `TryAddEnumerable`. Pick an `Order` outside the built-in chain's `[100_000_000, 900_000_000]` window.
- **Custom trait** — define a marker interface, write an advisor that checks `entity is IMyTrait`, and
  register it. The framework treats it like any built-in trait.
- **Suppression** — call the matching `Suppress*()` scope on the repository before a mutation to skip a
  specific advisor for the duration of the returned handle. See
  [repository/overview.md](../repository/overview.md) for the full table.

## Design rationale

Traits keep entity classes free of framework dependencies beyond the interface declarations. The
advisor pipeline is the only place framework logic runs; the entity stays a plain data container,
which keeps it easy to test, serialize, and share across assembly boundaries.

## Caveats

- Trait interfaces declare `get; set;` properties. Implementing one as an `init`-only property breaks
  the advisor that writes to it at runtime.
- `IConcurrency.Timestamp` is a non-nullable `Guid`; `Guid.Empty` denotes an unstamped entity.

## See also

- [traits.md](traits.md) — full trait reference with advisor order numbers
- [query-cache.md](query-cache.md) — `Schemata.Entity.Cache` advisors, reverse index, committed eviction
- [repository/mutation-pipeline.md](../repository/mutation-pipeline.md) — add/update/remove advisor chains
- [repository/query-pipeline.md](../repository/query-pipeline.md) — build-query/query/result advisor chains
