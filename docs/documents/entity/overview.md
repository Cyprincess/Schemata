# Entity Overview

Schemata entities are plain C# classes or records that carry their persistence and API semantics through marker interfaces called traits. A trait is a small interface that declares one or more properties. Traits do nothing on their own — behavior comes from advisors registered alongside the repository pipeline, and each advisor checks for the matching trait with a plain `is`-test inside its `AdviseAsync` method (or via a constrained generic type parameter). The repository setup wires the built-in trait advisors automatically; custom traits require a matching custom advisor registration. No base class or code generation is needed for the traits themselves.

## Where the code lives

| Item | Path |
|---|---|
| Trait interfaces | `src/Schemata.Abstractions/Entities/` |
| `IFreshness` (HTTP ETag) | `src/Schemata.Abstractions/Resource/IFreshness.cs` |
| `CanonicalNameAttribute` | `src/Schemata.Abstractions/Entities/CanonicalNameAttribute.cs` |
| Repository advisor implementations | `src/Schemata.Entity.Repository/Advisors/` |

## How traits activate behavior

Each trait interface is a contract between the entity and the advisor pipeline. When a repository operation runs, the advisor checks `entity is ITrait` (or `typeof(ITrait).IsAssignableFrom(typeof(TEntity))`). If the check passes, the advisor performs its work; otherwise it returns `AdviseResult.Continue` immediately.

The full trait-to-advisor mapping is documented in [traits.md](traits.md).

## Defining an entity

A minimal entity with timestamps, soft-delete, and a canonical name looks like this:

```csharp
[CanonicalName("publishers/{publisher}/books/{book}")]
public class Book : IIdentifier, ITimestamp, ISoftDelete, ICanonicalName
{
    public Guid      Uid          { get; set; }
    public string?   Name         { get; set; }
    public string?   CanonicalName { get; set; }
    public DateTime? CreateTime   { get; set; }
    public DateTime? UpdateTime   { get; set; }
    public DateTime? DeleteTime   { get; set; }
    public DateTime? PurgeTime    { get; set; }
}
```

No registration step is needed for the traits. Calling `services.AddRepository(typeof(MyRepository<,>))` registers all built-in advisors as open-generic scoped services, so every entity type that implements a trait gets the matching behavior automatically.

## Primary key convention

`RepositoryBase` resolves primary keys in two steps:

1. Read the class-level `[PrimaryKey(nameof(A), nameof(B))]` attribute (EF Core 7+).
2. Fall back to the `IIdentifier.Uid` property when no attribute is present.

`IIdentifier` uses `Guid` rather than `long` for AIP alignment and to support decentralized inserts without a database sequence. The `SchemataUser` identity entity bridges ASP.NET Core Identity's `IdentityUser<Guid>` by mapping `Id` to `Uid` via a `[NotMapped]` override.

## Trait composition

Traits are independent and compose freely. A single entity can implement any combination:

```csharp
public class Order : IIdentifier, ITimestamp, IConcurrency, ISoftDelete,
                     ICanonicalName, IOwnable, IDescriptive, ITransition
{
    // ...
}
```

The advisor pipeline runs each advisor in `Order` sequence. Advisors that don't apply to the entity return `Continue` in O(1) without touching the entity.

## Extension points

- **Custom advisors**: implement `IRepositoryAddAdvisor<TEntity>`, `IRepositoryUpdateAdvisor<TEntity>`, or `IRepositoryRemoveAdvisor<TEntity>` and register with `TryAddEnumerable`. Pick an `Order` outside the reserved range `[100_000_000, 900_000_000]`.
- **Custom traits**: define a marker interface, write an advisor that checks `entity is IMyTrait`, and register it. No framework changes required.
- **Suppression**: call the matching `Suppress*()` method on the repository before a mutation to skip a specific advisor for that operation. See [repository/overview.md](../repository/overview.md) for the full suppression table.

## Design motivation

Traits keep entity classes free of framework dependencies beyond the interface declarations. The advisor pipeline is the only place where framework logic runs; the entity itself is a plain data container. This makes entities easy to test, serialize, and share across assembly boundaries without pulling in the full framework.

## Caveats

- `IFreshness` lives in `Schemata.Abstractions/Resource/` alongside the resource contracts. It carries an HTTP ETag (AIP-154) on request and response DTOs.
- Trait interfaces declare properties with `get; set;`. Implementing them as `init`-only properties breaks the advisors that write to them at runtime.

## See also

- [traits.md](traits.md) — full trait reference with advisor order numbers
- [repository/overview.md](../repository/overview.md) — how the repository surfaces these traits
- [repository/mutation-pipeline.md](../repository/mutation-pipeline.md) — add/update/remove advisor chains
- [repository/query-pipeline.md](../repository/query-pipeline.md) — build-query/query/result advisor chains
- [core/advice-pipeline.md](../core/advice-pipeline.md) — advisor runtime mechanics
