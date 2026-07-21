# Soft Delete and Recovery

## What you'll build

An API where deleting a resource sets `DeleteTime` instead of removing the
row, normal queries exclude deleted rows automatically, and the built-in
`:undelete` method brings a resource back. The recipe uses the `Student` entity
from [Getting Started](../guides/getting-started.md) and walks through the
three advisors that implement AIP-164 soft delete, plus the suppression
markers you need when purging or listing tombstoned rows from application
code.

## Prerequisites

- Completed [Getting Started](../guides/getting-started.md) — `Student`
  already implements `ISoftDelete` and the EF Core repository is wired up.
- Soft-delete support is built into `Schemata.Entity.Repository`, so the
  getting-started packages already cover this recipe.

## Step 1 — Verify `ISoftDelete` is on the entity

The `Student` entity from the getting-started guide already implements
`ISoftDelete`:

```csharp
using Schemata.Abstractions.Entities;

[CanonicalName("students/{student}")]
public class Student : IIdentifier, ICanonicalName, ITimestamp, ISoftDelete
{
    public string? FullName { get; set; }
    public int     Age      { get; set; }

    public Guid Uid { get; set; }

    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }

    public DateTime? CreateTime { get; set; }
    public DateTime? UpdateTime { get; set; }

    // ISoftDelete
    public DateTime? DeleteTime { get; set; }
    public DateTime? PurgeTime  { get; set; }
}
```

`ISoftDelete` requires two properties:

| Property     | Meaning                                                                       |
| ------------ | ----------------------------------------------------------------------------- |
| `DeleteTime` | Non-null when the entity is logically deleted                                 |
| `PurgeTime`  | When the row will be permanently removed (optional; set by your purge policy) |

Three advisors activate automatically for any entity that implements
`ISoftDelete`:

| Advisor                               | Pipeline   | Order                | What it does                                                      |
| ------------------------------------- | ---------- | -------------------- | ----------------------------------------------------------------- |
| `AdviceAddSoftDelete<TEntity>`        | Add        | `Orders.Max` (900M)  | Clears `DeleteTime` to null on insert                             |
| `AdviceRemoveSoftDelete<TEntity>`     | Remove     | `Orders.Max` (900M)  | Sets `DeleteTime = UtcNow`, calls `UpdateAsync`, returns `Handle` |
| `AdviceBuildQuerySoftDelete<TEntity>` | BuildQuery | `Orders.Base` (100M) | Appends `WHERE DeleteTime IS NULL` to every query                 |

**Verify:** `DELETE /v1/students/{name}` returns 200 with the tombstoned row —
the body carries a non-null `delete_time`, per AIP-164. A subsequent
`GET /v1/students` filters the deleted student out of the results. A direct
database query confirms the row still exists with `DeleteTime` set.

## Step 2 — Understand the remove pipeline

When `repository.RemoveAsync(entity, ct)` is called, `AdviceRemoveSoftDelete`
intercepts it:

1. Checks `ctx.Has<SoftDeleteSuppressed>()` — if present, skips and lets the
   physical delete proceed.
2. Casts `entity` to `ISoftDelete`. If the entity doesn't implement it,
   skips.
3. Sets `entity.DeleteTime = DateTime.UtcNow`.
4. Calls `repository.UpdateAsync(entity, ct)` to persist the timestamp.
5. Returns `AdviseResult.Handle` — this signals the repository to skip the
   physical `DbContext.Remove` call.

The result is that the row is updated, not deleted. The EF Core repository
does not call `Context.Remove(entity)`.

## Step 3 — Restore a deleted student

Every `ISoftDelete` resource gets the AIP-164 `Undelete` method on its HTTP
surface for free — `SchemataResourceFeature` registers `UndeleteHandler` for
it during startup:

```shell
curl -X POST http://localhost:5000/v1/students/a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6:undelete \
     -H "Content-Type: application/json" \
     -d '{}'
```

The handler clears `DeleteTime` and `PurgeTime`, persists the update, and
returns the restored resource. Calling it on a live (undeleted) resource
throws `AlreadyExistsException`. Internally the restore is a plain
update — the method pipeline loads the tombstoned row with the query filter
suppressed, then the handler clears the two fields and commits — the restore
rides the ordinary update path end to end.

**Verify:** After deleting a student, `POST /v1/students/{name}:undelete`
returns 200 and the student reappears in `GET /v1/students`.

## Step 4 — Suppress soft delete for physical deletes

On the HTTP surface, `POST /v1/students/{name}:expunge` physically removes an
already-tombstoned row — it is registered automatically alongside `:undelete`.
For application code that needs to permanently remove a row, bypass the
soft-delete advisor by scoping `SuppressSoftDelete()` around the remove:

```csharp
public async Task PurgeAsync(Student student, CancellationToken ct)
{
    using (repository.SuppressSoftDelete())
    {
        await repository.RemoveAsync(student, ct);
    }
    await repository.CommitAsync(ct);
}
```

`SuppressSoftDelete()` sets `SoftDeleteSuppressed` in the `AdviceContext`.
`AdviceRemoveSoftDelete` checks `ctx.Has<SoftDeleteSuppressed>()` at the top
of `AdviseAsync` and returns `Continue`, letting the physical delete proceed.

**Verify:** After calling `PurgeAsync`, the row is gone from the database
entirely.

## Step 5 — List deleted entities (admin view)

To list soft-deleted students, suppress the query filter and add an explicit
`DeleteTime != null` predicate:

```csharp
public async Task<List<Student>> ListDeletedAsync(CancellationToken ct)
{
    using (repository.SuppressQuerySoftDelete())
    {
        return await repository
            .ListAsync<Student>(q => q.Where(s => s.DeleteTime != null), ct)
            .ToListAsync(ct);
    }
}
```

The explicit `s => s.DeleteTime != null` predicate filters to only deleted
rows. Without it, suppressing the filter would return live and deleted rows
together.

**Verify:** `ListDeletedAsync` returns only students with a non-null
`DeleteTime`.

## Common pitfalls

**`AdviceRemoveSoftDelete` returns `Handle`, not `Continue`.** The `Handle`
result tells the repository pipeline that the remove has been handled and the
physical delete should be skipped. If you write a custom remove advisor that
runs after `AdviceRemoveSoftDelete` (Order > 900M), it will not be called
because the pipeline short-circuits on `Handle`. Place custom remove advisors
at a lower `Order` value.

**Scope `SuppressQuerySoftDelete` with `using`.** The returned `IDisposable`
restores the prior state on dispose, so the filter applies normally again after
the block. Leaving the marker set on a long-lived repository would expose
tombstoned rows to later queries.

**`AdviceAddSoftDelete` clears `DeleteTime` on insert.** Seeding data with a
pre-set `DeleteTime` (e.g., importing archived records) loses it unless you
scope `SuppressSoftDelete()` around the `AddAsync`.

**`PurgeTime` is a convention field.** The framework stores it and leaves
enforcement to you. The built-in `POST /v1/students:purge` method is
filter-driven (AIP-165) — it removes rows matching the request filter on
demand, and an optional `parent` narrows the purge to that parent's child
collection. Sending `force = false` runs a preview: the response carries the
count and a sample of up to 100 matching resource names, and nothing is
expunged. Time-based reaping needs a scheduled job of your own that queries for
rows where `PurgeTime <= UtcNow` and calls `RemoveAsync` with
`SuppressSoftDelete()` scoped around it.

**Undelete rides the update pipeline.** The restore goes through the update
advisor pipeline, so add-time advisors such as `AdviceAddSoftDelete` stay out
of the run. Fields your add pipeline set at creation (e.g., `CanonicalName`
via `AdviceAddCanonicalName`) keep whatever values the entity had when it was
first created.

## See also

- [Getting Started](../guides/getting-started.md) — `Student` entity with
  `ISoftDelete`
- [Traits document](../documents/entity/traits.md) — `ISoftDelete` and all
  other entity traits
- [Mutation Pipeline document](../documents/repository/mutation-pipeline.md)
  — advisor execution order for add, update, and remove
- [Query Pipeline document](../documents/repository/query-pipeline.md) —
  `BuildQuery` advisor chain and suppression markers
