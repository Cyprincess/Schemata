# Entity Traits

Traits are marker interfaces that add cross-cutting behavior to entities. Each is delivered by an
advisor registered with the repository pipeline: the advisor checks the entity with an `is`-test (or a
constrained generic parameter) and runs its logic when the test matches. For the built-in traits below,
`AddRepository` wires the matching advisors — implementing the interface is the only per-entity step.
A custom trait needs both the interface and an advisor that performs the same kind of check.

The traits live in `Schemata.Abstractions.Entities`. Their built-in advisors live in three packages:
the always-on repository advisors in `Schemata.Entity.Repository`, the ownership advisors in
`Schemata.Entity.Owner` (activated by `UseOwner()`), and the query-cache advisors in
`Schemata.Entity.Cache` (activated by `UseQueryCache()`).

## Trait-to-advisor summary

| Trait              | Built-in advisor                      | Pipeline   | Order       |
| ------------------ | ------------------------------------- | ---------- | ----------- |
| `IIdentifier`      | —                                     | —          | —           |
| `ITimestamp`       | `AdviceAddTimestamp<TEntity>`         | Add        | 100,000,000 |
| `ITimestamp`       | `AdviceUpdateTimestamp<TEntity>`      | Update     | 100,000,000 |
| `IConcurrency`     | `AdviceAddConcurrency<TEntity>`       | Add        | 110,000,000 |
| `ICanonicalName`   | `AdviceAddCanonicalName<TEntity>`     | Add        | 120,000,000 |
| `ISoftDelete`      | `AdviceBuildQuerySoftDelete<TEntity>` | BuildQuery | 100,000,000 |
| `ISoftDelete`      | `AdviceAddSoftDelete<TEntity>`        | Add        | 900,000,000 |
| `ISoftDelete`      | `AdviceRemoveSoftDelete<TEntity>`     | Remove     | 900,000,000 |
| `IOwnable`         | `AdviceAddOwner<TEntity>`             | Add        | 130,000,000 |
| `IOwnable`         | `AdviceBuildQueryOwner<TEntity>`      | BuildQuery | 110,000,000 |
| `IExpiration`      | —                                     | —          | —           |
| `IDescriptive`     | —                                     | —          | —           |
| `IAnnotatable`     | —                                     | —          | —           |
| `IStateful`        | —                                     | —          | —           |
| `ITransition`      | —                                     | —          | —           |
| `ISourceReference` | —                                     | —          | —           |

Traits with no built-in advisor are data contracts: application code or other subsystems read and
write their properties. `IConcurrency` enforcement on update is provider-level, not an advisor — see
the `IConcurrency` section.

## IIdentifier

```csharp
public interface IIdentifier
{
    Guid Uid { get; set; }
}
```

Provides a `Guid` primary key. `RepositoryBase.ResolveKeyProperties` falls back to `Uid` when no
class-level `[PrimaryKey]` attribute resolves. No advisor assigns `Uid`; application code or a custom
add advisor sets it before `AddAsync`.

## ITimestamp

```csharp
public interface ITimestamp
{
    DateTime? CreateTime { get; set; }
    DateTime? UpdateTime { get; set; }
}
```

Records creation and last-update times, per AIP-148 `create_time` / `update_time`.

| Advisor                          | Pipeline | Order       | Behavior                                                                                                                   |
| -------------------------------- | -------- | ----------- | -------------------------------------------------------------------------------------------------------------------------- |
| `AdviceAddTimestamp<TEntity>`    | Add      | 100,000,000 | Sets `CreateTime` and `UpdateTime` to one reading of the injected `TimeProvider`'s UTC clock, so both are equal on create. |
| `AdviceUpdateTimestamp<TEntity>` | Update   | 100,000,000 | Sets `UpdateTime` to the current UTC time.                                                                                 |

Both advisors skip when `TimestampSuppressed` is present (call `repository.SuppressTimestamp()`).

## IConcurrency

```csharp
public interface IConcurrency
{
    Guid Timestamp { get; set; }
}
```

Supports optimistic concurrency via a GUID version token, per AIP-154. The property is named
`Timestamp` but holds a `Guid`, not a time value. `Guid.Empty` denotes an unstamped entity.

| Advisor                         | Pipeline | Order       | Behavior                                        |
| ------------------------------- | -------- | ----------- | ----------------------------------------------- |
| `AdviceAddConcurrency<TEntity>` | Add      | 110,000,000 | Mints a new GUID and assigns it to `Timestamp`. |

There is no add-side suppress flag and no update advisor. The update check is enforced by the database,
gated on the concrete entity annotating its `Timestamp` with `[ConcurrencyCheck]`
(`System.ComponentModel.DataAnnotations`). The attribute on the interface does not flow to the
implementation, so each consuming entity declares it:

```csharp
[ConcurrencyCheck]
public Guid Timestamp { get; set; }
```

- **EF Core** reads `[ConcurrencyCheck]` natively. `UpdateAsync` detaches the entity, re-attaches it as
  modified, and bumps `Timestamp` to a fresh GUID as the current value while the incoming token stays
  the original. `SaveChangesAsync` then issues `UPDATE ... WHERE <key> AND Timestamp = @original`; a
  zero-row result raises `DbUpdateConcurrencyException`, normalized to `AbortedException`.
- **LinqToDB** maps `[ConcurrencyCheck]` to an optimistic-lock column through the metadata reader
  registered by `UseLinqToDb`, and `UpdateAsync` calls `UpdateOptimisticAsync`. A zero-row result
  raises `AbortedException`.

Without `[ConcurrencyCheck]`, the update writes unconditionally; concurrent writers can lose updates.
The add stamp alone does not guard the update path.

## ICanonicalName

```csharp
public interface ICanonicalName
{
    string? Name          { get; set; }
    string? CanonicalName { get; set; }
}
```

Provides a fully-qualified resource name per AIP-122. `Name` is the short identifier segment;
`CanonicalName` is the full collection-relative path (e.g., `publishers/acme/books/les-miserables`).
Declare the pattern on the entity class:

```csharp
[CanonicalName("publishers/{publisher}/books/{book}")]
public class Book : ICanonicalName { /* ... */ }
```

| Advisor                           | Pipeline | Order       | Behavior                                                                                                                                |
| --------------------------------- | -------- | ----------- | --------------------------------------------------------------------------------------------------------------------------------------- |
| `AdviceAddCanonicalName<TEntity>` | Add      | 120,000,000 | Resolves the `[CanonicalName]` pattern against entity properties via `ResourceNameDescriptor` and writes the result to `CanonicalName`. |

The advisor has no suppress flag and runs whenever the entity implements `ICanonicalName` and its type
carries a registered pattern.

## ISoftDelete

```csharp
public interface ISoftDelete
{
    DateTime? DeleteTime { get; set; }
    DateTime? PurgeTime  { get; set; }
}
```

Enables soft deletion per AIP-164: a delete sets `DeleteTime` instead of removing the row. `PurgeTime`
is an optional scheduled permanent-removal time the framework does not enforce.

| Advisor                               | Pipeline   | Order       | Behavior                                                                                                                                             |
| ------------------------------------- | ---------- | ----------- | ---------------------------------------------------------------------------------------------------------------------------------------------------- |
| `AdviceBuildQuerySoftDelete<TEntity>` | BuildQuery | 100,000,000 | Appends `.Where(e => e.DeleteTime == null)` to every query.                                                                                          |
| `AdviceAddSoftDelete<TEntity>`        | Add        | 900,000,000 | Clears `DeleteTime` to `null` so a newly added entity is never marked deleted.                                                                       |
| `AdviceRemoveSoftDelete<TEntity>`     | Remove     | 900,000,000 | Sets `DeleteTime` to the current UTC time, calls `repository.UpdateAsync(entity)`, and returns `AdviseResult.Handle` to prevent the physical delete. |

The add and remove advisors skip when `SoftDeleteSuppressed` is present
(`repository.SuppressSoftDelete()`). The query filter skips separately under `QuerySoftDeleteSuppressed`
(`repository.SuppressQuerySoftDelete()`).

## IOwnable

```csharp
public interface IOwnable
{
    string? Owner { get; set; }
}
```

Records the canonical name of the principal that owns the entity (e.g., `users/chino`). The
`Schemata.Entity.Owner` package supplies two advisors, both registered by `UseOwner()`:

| Advisor                          | Pipeline   | Order       | Behavior                                                                           |
| -------------------------------- | ---------- | ----------- | ---------------------------------------------------------------------------------- |
| `AdviceAddOwner<TEntity>`        | Add        | 130,000,000 | Calls `IOwnerResolver<TEntity>.ResolveAsync` and assigns `Owner` when it is unset. |
| `AdviceBuildQueryOwner<TEntity>` | BuildQuery | 110,000,000 | Appends `.Where(e => e.Owner == owner)` to every query.                            |

Both consult `SchemataOwnerOptions.OnNullOwner` when the resolver returns `null`: `Reject` (default)
throws `PermissionDeniedException`, `EmptyResult` blocks, `AllowAll` continues. See
[ownership.md](../repository/ownership.md).

## IExpiration

```csharp
public interface IExpiration
{
    DateTime? ExpireTime { get; set; }
}
```

Marks an entity that expires at a scheduled time, per AIP-214 `expire_time`. Application code sets
`ExpireTime`; no built-in advisor reads or purges it.

## IDescriptive

```csharp
public interface IDescriptive
{
    string?                      DisplayName  { get; set; }
    Dictionary<string, string?>? DisplayNames { get; set; }
    string?                      Description  { get; set; }
    Dictionary<string, string?>? Descriptions { get; set; }
}
```

Provides user-facing display names and descriptions per AIP-148. `DisplayNames` and `Descriptions` are
localized variants keyed by IETF BCP 47 language tag (e.g., `"en"`, `"zh-Hans"`). No built-in advisor.

## IAnnotatable

```csharp
using System.Collections.Generic;

public interface IAnnotatable
{
    Dictionary<string, string?> Annotations { get; set; }
}
```

Declares the client-managed annotations map, per AIP-148 `annotations`. Clients store small amounts
of arbitrary data under string keys; the framework and engines only persist and serve the map. The
property is persisted as a JSON text column through the provider dictionary conversion
(`SchemataModelCustomizer` on EF Core, the metadata reader on LINQ to DB). No built-in advisor.

## IStateful

```csharp
public interface IStateful
{
    string? State { get; set; }
}
```

Declares a discrete workflow or lifecycle state, per AIP-216 `state`. No built-in repository advisor.

## ITransition

```csharp
public interface ITransition
{
    string  Event     { get; set; }
    string? Note      { get; set; }
    string? UpdatedBy { get; set; }
}
```

Records an audit event: `Event` is the event-type identifier (e.g., `created`, `updated`), `Note` an
optional human-readable note, `UpdatedBy` the canonical name of the principal who triggered it. Pair
with `ITimestamp` for a complete audit record. No built-in advisor.

## ISourceReference

```csharp
public interface ISourceReference
{
    string? SourceType      { get; set; }
    string? Source          { get; set; }
    Guid?   SourceTimestamp { get; set; }
}
```

A weak-consistency reference from a derived row (event, audit entry) back to its originating entity:
`SourceType` is the source CLR `Type.FullName`, `Source` its `ICanonicalName.CanonicalName`, and
`SourceTimestamp` a snapshot of its `IConcurrency.Timestamp` at capture time. All three are nullable so
rows with no semantic source leave them empty. No built-in repository advisor.

## Common combinations

| Pattern          | Traits                                       |
| ---------------- | -------------------------------------------- |
| Basic entity     | `IIdentifier` + `ITimestamp` + `ISoftDelete` |
| Concurrency-safe | adds `IConcurrency`                          |
| Named resource   | adds `ICanonicalName`                        |
| Owned resource   | adds `IOwnable`                              |
| Audited resource | adds `ITransition`                           |

## See also

- [overview.md](overview.md) — entity design and primary-key convention
- [repository/mutation-pipeline.md](../repository/mutation-pipeline.md) — add/update/remove advisor chains
- [repository/query-pipeline.md](../repository/query-pipeline.md) — build-query/query/result advisor chains
