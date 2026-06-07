# Entity Traits

Traits are marker interfaces that add cross-cutting behavior to entities. The behavior is delivered by ordinary advisors registered alongside the repository pipeline: each advisor checks the entity with a plain `is`-test inside its `AdviseAsync` method (or via a constrained generic type parameter), then runs its logic when the test matches. For the built-in traits listed below, the repository setup wires the matching advisors automatically — no per-entity step is needed beyond implementing the interface. For custom traits, defining the interface is only half the work; behavior requires registering an advisor that performs the same kind of check.

Traits live in two packages depending on where they are applied:

| Package | Applied to | Traits |
|---|---|---|
| `Schemata.Abstractions.Entities` | Entity classes | `IIdentifier`, `ITimestamp`, `ISoftDelete`, `IConcurrency`, `ICanonicalName`, `IDescriptive`, `ITransition`, `IOwnable`, `IStateful`, `IExpiration` |
| `Schemata.Abstractions.Resource` | Request/response DTOs | `IFreshness` |

`IFreshness` lives in `Schemata.Abstractions/Resource/IFreshness.cs` as an HTTP-layer concern (ETag / If-Match) carried by request and response DTOs.

## Where the code lives

| Trait | Source file |
|---|---|
| `IIdentifier` | `src/Schemata.Abstractions/Entities/IIdentifier.cs` |
| `ITimestamp` | `src/Schemata.Abstractions/Entities/ITimestamp.cs` |
| `ISoftDelete` | `src/Schemata.Abstractions/Entities/ISoftDelete.cs` |
| `IConcurrency` | `src/Schemata.Abstractions/Entities/IConcurrency.cs` |
| `ICanonicalName` | `src/Schemata.Abstractions/Entities/ICanonicalName.cs` |
| `CanonicalNameAttribute` | `src/Schemata.Abstractions/Entities/CanonicalNameAttribute.cs` |
| `IDescriptive` | `src/Schemata.Abstractions/Entities/IDescriptive.cs` |
| `ITransition` | `src/Schemata.Abstractions/Entities/ITransition.cs` |
| `IOwnable` | `src/Schemata.Abstractions/Entities/IOwnable.cs` |
| `IStateful` | `src/Schemata.Abstractions/Entities/IStateful.cs` |
| `IExpiration` | `src/Schemata.Abstractions/Entities/IExpiration.cs` |
| `IFreshness` | `src/Schemata.Abstractions/Resource/IFreshness.cs` |
| Built-in advisors | `src/Schemata.Entity.Repository/Advisors/` |

---

## IIdentifier

**File:** `src/Schemata.Abstractions/Entities/IIdentifier.cs`

```csharp
public interface IIdentifier
{
    Guid Uid { get; set; }
}
```

Provides a `Guid` primary key. `RepositoryBase` falls back to `Uid` when no `[PrimaryKey]` attribute is present on the entity class. `Guid` was chosen over `long` for AIP alignment and to support decentralized inserts without a database sequence.

**Built-in advisors:** None. `Uid` is assigned by application code or a custom advisor before `AddAsync` is called.

---

## ITimestamp

**File:** `src/Schemata.Abstractions/Entities/ITimestamp.cs`

```csharp
public interface ITimestamp
{
    DateTime? CreateTime { get; set; }
    DateTime? UpdateTime { get; set; }
}
```

Records creation and last-update times, corresponding to AIP-148 `create_time` and `update_time`.

**Built-in advisors:**

| Advisor | Pipeline | Order | Behavior |
|---|---|---|---|
| `AdviceAddTimestamp<TEntity>` | Add | 100,000,000 | Sets `CreateTime` and `UpdateTime` to `DateTime.UtcNow`. |
| `AdviceUpdateTimestamp<TEntity>` | Update | 100,000,000 | Sets `UpdateTime` to `DateTime.UtcNow`. |

Both advisors are suppressed when `TimestampSuppressed` is present in the advice context (call `repository.SuppressTimestamp()`).

---

## ISoftDelete

**File:** `src/Schemata.Abstractions/Entities/ISoftDelete.cs`

```csharp
public interface ISoftDelete
{
    DateTime? DeleteTime { get; set; }
    DateTime? PurgeTime  { get; set; }
}
```

Enables soft deletion per AIP-164. Instead of physically removing a row, the entity is flagged with a deletion timestamp. `PurgeTime` is an optional scheduled permanent-removal time.

**Built-in advisors:**

| Advisor | Pipeline | Order | Behavior |
|---|---|---|---|
| `AdviceAddSoftDelete<TEntity>` | Add | 900,000,000 | Clears `DeleteTime` to `null` so newly added entities are never marked deleted. |
| `AdviceRemoveSoftDelete<TEntity>` | Remove | 900,000,000 | Sets `DeleteTime = DateTime.UtcNow`, calls `repository.UpdateAsync(entity)`, returns `AdviseResult.Handle` to prevent the physical delete. |
| `AdviceBuildQuerySoftDelete<TEntity>` | BuildQuery | 100,000,000 | Appends `.Where(e => e.DeleteTime == null)` to every query. |

`AdviceAddSoftDelete` and `AdviceRemoveSoftDelete` are suppressed by `SoftDeleteSuppressed` (call `repository.SuppressSoftDelete()`). The query filter is suppressed separately by `QuerySoftDeleteSuppressed` (call `repository.SuppressQuerySoftDelete()`).

---

## IConcurrency

**File:** `src/Schemata.Abstractions/Entities/IConcurrency.cs`

```csharp
public interface IConcurrency
{
    Guid? Timestamp { get; set; }
}
```

Supports optimistic concurrency control via a GUID version token, per AIP-154. The field is named `Timestamp` for historical reasons; it is not a time value.

**Built-in advisors:**

| Advisor | Pipeline | Order | Behavior |
|---|---|---|---|
| `AdviceAddConcurrency<TEntity>` | Add | 110,000,000 | Mints a new `Guid` and assigns it to `Timestamp`. |
| `AdviceUpdateConcurrency<TEntity>` | Update | 900,000,000 | Loads the stored entity via `repository.GetAsync`, compares `Timestamp` values, throws `ConcurrencyException` on mismatch, then mints a new `Guid`. |

`AdviceUpdateConcurrency` reads the stored row to compare `Timestamp`. Like several other repository-layer behaviours, that read leaves a tracked instance in the EF Core change tracker. The EF Core provider's `UpdateAsync` defends against this by calling `Detach(entity)` before `Context.Update(entity)`; see [Detach before Update](../repository/providers.md#detach-before-update) for the full set of paths it covers.

Both advisors are suppressed by `ConcurrencySuppressed` (call `repository.SuppressConcurrency()`).

`IConcurrency` is the entity-side counterpart of `IFreshness`. The resource layer computes weak ETags from `Timestamp` and writes them to the response DTO's `EntityTag`.

---

## ICanonicalName

**File:** `src/Schemata.Abstractions/Entities/ICanonicalName.cs`

```csharp
public interface ICanonicalName
{
    string? Name          { get; set; }
    string? CanonicalName { get; set; }
}
```

Provides a fully-qualified resource name following AIP-122. `Name` is the short identifier segment; `CanonicalName` is the full path (e.g., `publishers/acme/books/les-miserables`).

Declare the pattern on the entity class:

```csharp
[CanonicalName("publishers/{publisher}/books/{book}")]
public class Book : ICanonicalName { ... }
```

`CanonicalNameAttribute` (`src/Schemata.Abstractions/Entities/CanonicalNameAttribute.cs`) stores the pattern string. `ResourceNameDescriptor.Resolve` substitutes placeholder segments from the entity's properties at add time.

**Built-in advisors:**

| Advisor | Pipeline | Order | Behavior |
|---|---|---|---|
| `AdviceAddCanonicalName<TEntity>` | Add | 220,000,000 | Reads the `[CanonicalName]` pattern, resolves placeholders against entity properties, writes the result to `CanonicalName`. |

No suppress flag exists for this advisor; it always runs when the entity implements `ICanonicalName` and has a registered pattern.

---

## IDescriptive

**File:** `src/Schemata.Abstractions/Entities/IDescriptive.cs`

```csharp
public interface IDescriptive
{
    string?                     DisplayName  { get; set; }
    Dictionary<string, string>? DisplayNames { get; set; }
    string?                     Description  { get; set; }
    Dictionary<string, string>? Descriptions { get; set; }
}
```

Provides user-facing display names and descriptions per AIP-148. `DisplayNames` and `Descriptions` are localized variants keyed by IETF BCP 47 language tag (e.g., `"en"`, `"zh-Hans"`).

**Built-in advisors:** None. Application code sets these fields during create and update.

---

## ITransition

**File:** `src/Schemata.Abstractions/Entities/ITransition.cs`

```csharp
public interface ITransition
{
    string  Event     { get; set; }
    string? Note      { get; set; }
    string? UpdatedBy { get; set; }
}
```

Marks an entity as an audit-log entry. `Event` is the event type identifier (e.g., `created`, `updated`). `UpdatedBy` is the canonical resource name of the principal who triggered the event.

Pair with `ITimestamp` for a complete audit record: `CreateTime` records when the event occurred, `Event` records what happened, `UpdatedBy` records who did it.

**Built-in advisors:** None. Transition records are created by application code or advisors.

---

## IOwnable

**File:** `src/Schemata.Abstractions/Entities/IOwnable.cs`

```csharp
public interface IOwnable
{
    string? Owner { get; set; }
}
```

Records the canonical name of the principal that owns the entity (e.g., `users/chino`). The `Schemata.Entity.Owner` package provides two advisors that activate when `UseOwner()` is called on the repository builder:

| Advisor | Pipeline | Order | Behavior |
|---|---|---|---|
| `AdviceAddOwner<TEntity>` | Add | 230,000,000 | Calls `IOwnerResolver<TEntity>.ResolveAsync` and sets `Owner` if not already set. |
| `AdviceBuildQueryOwner<TEntity>` | BuildQuery | 110,000,000 | Appends `.Where(e => e.Owner == owner)` to every query. |

Both advisors consult `SchemataOwnerOptions.OnNullOwner` when the resolver returns `null`: `Reject` throws `AuthorizationException`, `EmptyResult` returns `Block`, `AllowAll` continues. The default is `Reject`.

See [ownership.md](../repository/ownership.md) for registration and extension details.

---

## IStateful

**File:** `src/Schemata.Abstractions/Entities/IStateful.cs`

Indicates that an entity has a discrete lifecycle state. The `State` property holds the current state name. The Flow engine reads and writes this property during process execution.

**Built-in advisors:** None in the repository package.

---

## IExpiration

**File:** `src/Schemata.Abstractions/Entities/IExpiration.cs`

Marks an entity that can expire at a scheduled time. `ExpireTime` is set by application code; the framework does not automatically purge expired entities.

**Built-in advisors:** None.

---

## IFreshness

**File:** `src/Schemata.Abstractions/Resource/IFreshness.cs`

```csharp
public interface IFreshness
{
    string? EntityTag { get; set; }
}
```

Carries an HTTP ETag for conditional requests (`If-Match` / `If-None-Match`) per AIP-154. Implemented on request and response DTOs. The built-in freshness advisors honor only weak validators (values beginning with `W/`); missing or non-`W/` tags are treated as "client did not opt into freshness validation."

`IFreshness` depends on the entity implementing `IConcurrency`. Without a `Timestamp` on the entity, the freshness advisors short-circuit.

**Built-in advisors** (in `Schemata.Resource.Foundation`):

| Advisor | Pipeline | Order | Behavior |
|---|---|---|---|
| `AdviceUpdateFreshness` | Update request | 300,000,000 | Compares the request ETag against the stored `IConcurrency.Timestamp`; throws `ConcurrencyException` on mismatch. |
| `AdviceDeleteFreshness` | Delete request | 300,000,000 | Same check for delete operations. |
| `AdviceResponseFreshness` | Response | 100,000,000 | Computes a weak ETag from `IConcurrency.Timestamp` and writes it to the detail DTO's `EntityTag`. |

---

## Common combinations

| Pattern | Traits |
|---|---|
| Basic entity | `IIdentifier` + `ITimestamp` + `ISoftDelete` |
| Concurrency-safe | adds `IConcurrency` |
| Named resource | adds `ICanonicalName` |
| Owned resource | adds `IOwnable` |
| Audited resource | adds `ITransition` |

## See also

- [overview.md](overview.md) — entity design philosophy and primary key convention
- [repository/mutation-pipeline.md](../repository/mutation-pipeline.md) — add/update/remove advisor chains with order numbers
- [repository/query-pipeline.md](../repository/query-pipeline.md) — build-query/query/result advisor chains
- [repository/ownership.md](../repository/ownership.md) — `IOwnable` registration and `IOwnerResolver`
- [core/advice-pipeline.md](../core/advice-pipeline.md) — advisor runtime mechanics and `AdviseResult` semantics
