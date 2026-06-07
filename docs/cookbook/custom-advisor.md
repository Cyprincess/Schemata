# Custom Advisor

## What you'll build

A `SlugNormalizeAdvisor<TEntity>` that lower-cases the `Name` field on every update, registered open-generic so it applies to any entity that implements `ICanonicalName`. You'll also write an integration test that confirms the advisor runs and the database row reflects the normalized value.

## Prerequisites

- The Student example from [Getting Started](../guides/getting-started.md) is running.
- NuGet packages: `Schemata.Entity.Repository`, `Schemata.Abstractions`.
- For the integration test: `Microsoft.EntityFrameworkCore.InMemory`, `Microsoft.AspNetCore.Mvc.Testing`, `xunit`.

## Step 1: Implement the advisor

`IRepositoryUpdateAdvisor<TEntity>` is the correct interface for logic that runs before an entity is persisted during an update. It extends `IAdvisor<IRepository<TEntity>, TEntity>`, which means the pipeline passes the repository instance and the entity being updated.

```csharp
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

public sealed class SlugNormalizeAdvisor<TEntity> : IRepositoryUpdateAdvisor<TEntity>
    where TEntity : class, ICanonicalName
{
    public int Order => 50_000_000;   // runs before AdviceUpdateTimestamp (100M)

    public Task<AdviseResult> AdviseAsync(
        AdviceContext      ctx,
        IRepository<TEntity> repository,
        TEntity            entity,
        CancellationToken  ct = default
    ) {
        if (!string.IsNullOrWhiteSpace(entity.Name)) {
            entity.Name = entity.Name.ToLowerInvariant();
        }

        return Task.FromResult(AdviseResult.Continue);
    }
}
```

Return `AdviseResult.Continue` to let the rest of the pipeline proceed. Return `AdviseResult.Block` to abort the update without persisting. Return `AdviseResult.Handle` only when your advisor has already performed the persistence itself and the repository should skip its own write.

**Assertion:** The class compiles with no errors. The `Order` value is below 100_000_000, placing it before all built-in repository advisors.

## Step 2: Register the advisor

Use `TryAddEnumerable` with an open-generic `ServiceDescriptor`. This registers the advisor for every `TEntity` that satisfies the constraint without requiring per-entity registration.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Entity.Repository.Advisors;

// In your startup or extension method:
services.TryAddEnumerable(
    ServiceDescriptor.Scoped(
        typeof(IRepositoryUpdateAdvisor<>),
        typeof(SlugNormalizeAdvisor<>)
    )
);
```

Place this call inside a `Use*` extension method on `SchemataBuilder`, or inside a feature's `ConfigureServices`. Do not call it directly on the host `IServiceCollection` from `Program.cs` — write it into the `schema.Services` buffer instead:

```csharp
schema.Services.TryAddEnumerable(
    ServiceDescriptor.Scoped(
        typeof(IRepositoryUpdateAdvisor<>),
        typeof(SlugNormalizeAdvisor<>)
    )
);
```

**Assertion:** Resolve `IEnumerable<IRepositoryUpdateAdvisor<Student>>` from the DI container. The collection contains a `SlugNormalizeAdvisor<Student>` instance.

## Step 3: Write an integration test

The test spins up a real in-memory EF Core context and exercises the full repository pipeline, including the advisor.

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Xunit;

public sealed class SlugNormalizeAdvisorShould
{
    [Fact]
    public async Task NormalizesNameToLowercase_OnUpdate()
    {
        var services = new ServiceCollection();

        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase("slug-test"));
        services.AddRepository<EntityFrameworkCoreRepository<AppDbContext>>();
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped(
                typeof(IRepositoryUpdateAdvisor<>),
                typeof(SlugNormalizeAdvisor<>)
            )
        );

        await using var sp = services.BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IRepository<Student>>();

        // Seed
        var student = new Student { Name = "alice", CanonicalName = "students/alice" };
        await repo.AddAsync(student);
        await repo.CommitAsync();

        // Update with mixed-case name
        student.Name = "ALICE-UPDATED";
        await repo.UpdateAsync(student);
        await repo.CommitAsync();

        // Verify
        var found = await repo.FirstOrDefaultAsync<Student>(s => s.Name == "alice-updated");
        Assert.NotNull(found);
        Assert.Equal("alice-updated", found.Name);
    }
}
```

**Assertion:** The test passes. `found.Name` is `"alice-updated"`, not `"ALICE-UPDATED"`.

## Step 4: Confirm ordering relative to built-in advisors

The built-in update advisors and their `Order` values are:

| Advisor | Order |
| --- | --- |
| `AdviceUpdateTimestamp` | 100_000_000 (`Orders.Base`) |
| `AdviceUpdateValidation` | 110_000_000 |
| `AdviceUpdateConcurrency` | 900_000_000 (`Orders.Max`) |

`SlugNormalizeAdvisor` at `Order = 50_000_000` runs before all of them. To run between validation and concurrency, pick an `Order` in `[110_000_001, 899_999_999]` and outside the reserved range `[100_000_000, 900_000_000]` if your advisor ships in user code.

**Assertion:** Add a second advisor at `Order = 200_000_000` and confirm via a test that the slug normalization happens before the second advisor sees the entity.

## Common pitfalls

- **Registering as `AddScoped` instead of `TryAddEnumerable`.** `AddScoped(typeof(IRepositoryUpdateAdvisor<Student>), typeof(SlugNormalizeAdvisor<Student>))` replaces any existing registration for that closed type. `TryAddEnumerable` appends to the collection, which is what the pipeline expects.
- **Forgetting the open-generic constraint.** `SlugNormalizeAdvisor<TEntity>` has `where TEntity : class, ICanonicalName`. If you register it as `typeof(IRepositoryUpdateAdvisor<>)` against `typeof(SlugNormalizeAdvisor<>)`, the DI container will attempt to construct it for every entity type, including those that don't implement `ICanonicalName`. Add a null-check guard or split into a closed registration per entity type if the constraint is critical.
- **Returning `Handle` unintentionally.** `Handle` tells the repository to skip its own `Context.Update` call. If your advisor returns `Handle` without actually persisting the entity, the update is silently dropped.
- **Advisor not running in the resource pipeline.** The resource handler calls `IRepository<TEntity>.UpdateAsync`, which runs the advisor chain. If you bypass the repository and call `Context.Update` directly, advisors don't run.

## See also

- [Advice pipeline](../documents/core/advice-pipeline.md)
- [Repository mutation pipeline](../documents/repository/mutation-pipeline.md)
- [Entity traits](../documents/entity/traits.md)
