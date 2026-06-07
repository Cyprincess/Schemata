# Soft Delete and Recovery

## What you'll build

An API where deleting a resource sets `DeleteTime` instead of removing the
row, normal queries exclude deleted rows automatically, and a dedicated restore
endpoint clears `DeleteTime` to bring a resource back. The recipe uses the
`Student` entity from [Getting Started](../guides/getting-started.md) and
walks through the three advisors that implement AIP-164 soft delete, plus the
suppression mechanism needed to build the restore endpoint.

## Prerequisites

- Completed [Getting Started](../guides/getting-started.md) — `Student`
  already implements `ISoftDelete` and the EF Core repository is wired up.
- No additional packages are required; soft-delete support is built into
  `Schemata.Entity.Repository`.

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

| Property | Meaning |
| --- | --- |
| `DeleteTime` | Non-null when the entity is logically deleted |
| `PurgeTime` | When the row will be permanently removed (optional; set by your purge policy) |

Three advisors activate automatically for any entity that implements
`ISoftDelete`:

| Advisor | Pipeline | Order | What it does |
| --- | --- | --- | --- |
| `AdviceAddSoftDelete<TEntity>` | Add | `Orders.Max` (900M) | Clears `DeleteTime` to null on insert |
| `AdviceRemoveSoftDelete<TEntity>` | Remove | `Orders.Max` (900M) | Sets `DeleteTime = UtcNow`, calls `UpdateAsync`, returns `Handle` |
| `AdviceBuildQuerySoftDelete<TEntity>` | BuildQuery | `Orders.Base` (100M) | Appends `WHERE DeleteTime IS NULL` to every query |

**Verify:** `DELETE /students/{name}` returns 204. A subsequent
`GET /students` does not include the deleted student. A direct database query
confirms the row still exists with `DeleteTime` set.

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

## Step 3 — Build a restore endpoint

To restore a soft-deleted student, you need to:

1. Load the entity including soft-deleted rows (suppress the query filter).
2. Clear `DeleteTime`.
3. Persist via `UpdateAsync`.

Create a controller action that does this:

```csharp
using Microsoft.AspNetCore.Mvc;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

[ApiController]
public class StudentRestoreController(IRepository<Student> repository) : ControllerBase
{
    [HttpPost("{name=students/*}:restore")]
    public async Task<IActionResult> RestoreAsync(
        string            name,
        CancellationToken ct)
    {
        // Load the deleted entity — suppress the soft-delete query filter
        // so the WHERE DeleteTime IS NULL clause is not applied.
        var student = await repository
            .Once()
            .SuppressQuerySoftDelete()
            .FirstOrDefaultAsync<Student>(s => s.CanonicalName == name, ct);

        if (student is null)
            return NotFound();

        if (student.DeleteTime is null)
            return Conflict("Resource is not deleted.");

        student.DeleteTime = null;
        student.PurgeTime  = null;

        await repository.UpdateAsync(student, ct);
        await repository.CommitAsync(ct);

        return Ok(student);
    }
}
```

Key points:

- `Once()` creates a fresh repository instance with a fresh `AdviceContext`.
  This prevents the suppression from leaking into the request-scoped
  repository used by other operations in the same request.
- `SuppressQuerySoftDelete()` sets `QuerySoftDeleteSuppressed` on the new
  instance's `AdviceContext`. `AdviceBuildQuerySoftDelete` checks
  `ctx.Has<QuerySoftDeleteSuppressed>()` and skips the filter when it's
  present.
- Setting `DeleteTime = null` and calling `UpdateAsync` is the restore
  operation. There is no dedicated "undelete" method; it's a plain update.
- `CommitAsync` flushes the change to the database.

**Verify:** After deleting a student, `POST /students/{name}:restore` returns
200 and the student reappears in `GET /students`.

## Step 4 — Suppress soft delete for physical deletes

Some operations need to permanently remove a row, bypassing the soft-delete
advisor. Set `SoftDeleteSuppressed` on the `AdviceContext` before calling
`RemoveAsync`:

```csharp
public async Task PurgeAsync(Student student, CancellationToken ct)
{
    // ctx is the repository's AdviceContext
    repository.AdviceContext.Set<SoftDeleteSuppressed>();
    await repository.RemoveAsync(student, ct);
    await repository.CommitAsync(ct);
}
```

`AdviceRemoveSoftDelete` checks `ctx.Has<SoftDeleteSuppressed>()` at the top
of `AdviseAsync` and returns `Continue` immediately, allowing the physical
delete to proceed.

**Verify:** After calling `PurgeAsync`, the row is gone from the database
entirely.

## Step 5 — List deleted entities (admin view)

To list soft-deleted students, suppress the query filter on a fresh repository
instance:

```csharp
public IAsyncEnumerable<Student> ListDeletedAsync(CancellationToken ct)
{
    return repository
        .Once()
        .SuppressQuerySoftDelete()
        .ListAsync<Student>(s => s.DeleteTime != null, null, ct);
}
```

The explicit `s => s.DeleteTime != null` predicate filters to only deleted
rows. Without it, `SuppressQuerySoftDelete` would return all rows including
live ones.

**Verify:** `ListDeletedAsync` returns only students with a non-null
`DeleteTime`.

## Common pitfalls

**`AdviceRemoveSoftDelete` returns `Handle`, not `Continue`.** The `Handle`
result tells the repository pipeline that the remove has been handled and the
physical delete should be skipped. If you write a custom remove advisor that
runs after `AdviceRemoveSoftDelete` (Order > 900M), it will not be called
because the pipeline short-circuits on `Handle`. Place custom remove advisors
at a lower `Order` value.

**`SuppressQuerySoftDelete` must be called on `Once()`.** Calling it on the
request-scoped repository instance would suppress the filter for all
subsequent queries in that request, including those made by other services.
Always use `Once()` to get an isolated instance.

**`AdviceAddSoftDelete` clears `DeleteTime` on insert.** If you try to seed
data with a pre-set `DeleteTime` (e.g., to import archived records), the
advisor will clear it. Suppress it by setting `SoftDeleteSuppressed` on the
context before calling `AddAsync`.

**`PurgeTime` is not enforced by the framework.** The `PurgeTime` property is
a convention field. The framework does not automatically purge rows when
`PurgeTime` is reached. You must implement a background job or scheduled task
that queries for rows where `PurgeTime <= UtcNow` and calls `RemoveAsync`
with `SoftDeleteSuppressed` set.

**Restore does not re-run the add pipeline.** Clearing `DeleteTime` and
calling `UpdateAsync` goes through the update advisor pipeline, not the add
pipeline. `AdviceAddSoftDelete` does not run. If your add pipeline sets other
fields (e.g., `CanonicalName` via `AdviceAddCanonicalName`), those are not
re-applied on restore. The entity retains whatever values it had when it was
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
