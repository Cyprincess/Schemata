# Custom Advisor

## What you'll build

A `SlugNormalizeAdvisor<TEntity>` that lower-cases the `Name` field on every update, registered
open-generic so it applies to any entity implementing `ICanonicalName`. You'll also write an
integration test that confirms the advisor runs and the stored row reflects the normalized value.

The snippets and proposed test were source-traced but not compiled or executed in this alignment.
Keep the prerequisite web project's implicit imports; the separate test project needs `System`,
`System.Linq`, `System.Threading`, and `System.Threading.Tasks` imports shown below.

## Prerequisites

- The Student example from [Getting Started](../guides/getting-started.md) is running.
- NuGet packages: `Schemata.Entity.Repository`, `Schemata.Abstractions`.
- For the test: `Microsoft.EntityFrameworkCore.InMemory`, `xunit`.

## Step 1: Implement the advisor

`IRepositoryUpdateAdvisor<TEntity>` runs before an entity is persisted on update. It extends
`IAdvisor<IRepository<TEntity>, TEntity, MutationOptions>`, passing per-call options before cancellation.

```csharp
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

public sealed class SlugNormalizeAdvisor<TEntity> : IRepositoryUpdateAdvisor<TEntity>
    where TEntity : class, ICanonicalName
{
    public int Order => 50_000_000;   // runs before AdviceUpdateTimestamp (Orders.Base = 100M)

    public Task<AdviseResult> AdviseAsync(
        AdviceContext        ctx,
        IRepository<TEntity> repository,
        TEntity              entity,
        MutationOptions      options,
        CancellationToken    ct = default
    ) {
        if (!string.IsNullOrWhiteSpace(entity.Name)) {
            entity.Name = entity.Name.ToLowerInvariant();
        }

        return Task.FromResult(AdviseResult.Continue);
    }
}
```

Return `Continue` to let the rest of the pipeline proceed, `Block` to abort the update without
persisting, or `Handle` only when the advisor has already persisted the entity and the repository
should skip its own write.

**Expected contract (unexecuted):** `Order = 50_000_000` precedes the built-in update advisors.

## Step 2: Register the advisor

Use `TryAddEnumerable` with an open-generic `ServiceDescriptor`, which registers the advisor for
every `TEntity` that satisfies the constraint:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Entity.Repository.Advisors;

services.TryAddEnumerable(
    ServiceDescriptor.Scoped(
        typeof(IRepositoryUpdateAdvisor<>),
        typeof(SlugNormalizeAdvisor<>)));
```

Place the call inside a feature's `ConfigureServices`, or inside a `schema.ConfigureServices(...)`
block so it writes into the builder's staging collection rather than the live container:

```csharp
schema.ConfigureServices(services => {
    services.TryAddEnumerable(
        ServiceDescriptor.Scoped(
            typeof(IRepositoryUpdateAdvisor<>),
            typeof(SlugNormalizeAdvisor<>)));
});
```

**Assertion:** Resolving `IEnumerable<IRepositoryUpdateAdvisor<Student>>` yields a
`SlugNormalizeAdvisor<Student>` instance.

## Step 3: Write an integration test

The test runs a real in-memory EF Core context through the full repository pipeline, including the
advisor.

```csharp
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Xunit;

[Trait("Layer", "Integration")]
public sealed class SlugNormalizeAdvisorShould
{
    [Fact]
    public async Task Update_MixedCaseName_PersistsLowercaseName()
    {
        var services = new ServiceCollection();

        services.AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase("slug-test"));
        services.AddRepository<Student, EfCoreRepository<AppDbContext, Student>>();
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped(
                typeof(IRepositoryUpdateAdvisor<>),
                typeof(SlugNormalizeAdvisor<>)));

        await using var sp    = services.BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IRepository<Student>>();

        // Seed
        var student = new Student { Name = "alice", CanonicalName = "students/alice" };
        await repo.AddAsync(student);
        await repo.CommitAsync();

        // Update with a mixed-case name
        student.Name = "ALICE-UPDATED";
        await repo.UpdateAsync(student);
        await repo.CommitAsync();

        await using var verificationScope = sp.CreateAsyncScope();
        var verificationRepository = verificationScope.ServiceProvider.GetRequiredService<IRepository<Student>>();
        var found = await verificationRepository.FirstOrDefaultAsync<Student>(
            q => q.Where(s => s.Name == "alice-updated"));

        Assert.NotNull(found);
        Assert.Equal("alice-updated", found.Name);
    }
}
```

`AddRepository<Student, EfCoreRepository<AppDbContext, Student>>()` registers the closed-generic EF
Core repository for `Student`; `EfCoreRepository<TContext, TEntity>` resolves its context from the
`IDbContextFactory<AppDbContext>` registered by `AddDbContextFactory`.

**Expected result (unexecuted):** `found.Name` is `"alice-updated"`.

## Step 4: Ordering relative to built-in advisors

The built-in update advisors and their `Order` values:

| Advisor                  | Order                       |
| ------------------------ | --------------------------- |
| `AdviceUpdateTimestamp`  | 100,000,000 (`Orders.Base`) |
| `AdviceUpdateValidation` | 110,000,000                 |

`SlugNormalizeAdvisor` at `Order = 50_000_000` runs before both. To run after validation, pick an
`Order` above 110,000,000. The `[100_000_000, 900_000_000]` band reserves *feature* priorities —
it does not constrain advisor `Order`. An advisor's `Order` only fixes its position relative to the
built-in advisor chain, whose anchors come from `SchemataConstants.Orders` (`Base` = 100M for
built-in advisors, `Extension` = 400M as the extension-feature anchor, `Max` = 900M for advisors
that must run near the end). A value below `Base`, like the example's 50M, is legal and means
"before every built-in advisor". Providers own concurrency checking and stamp rotation at their
write boundary; `AdviceAddConcurrency` initializes the add-time stamp. Resource freshness advisors
compare request ETags before dispatch.

**Assertion:** Add a second advisor at `Order = 200_000_000` and confirm by test that slug
normalization happens before the second advisor sees the entity.

## Common pitfalls

- **Repeating registration.** `AddScoped` appends descriptors and enumerable resolution returns
  all registrations. `TryAddEnumerable` also appends, but deduplicates the same service and
  implementation type. Use it when repeated feature setup must not run the same advisor twice.
- **`FirstOrDefaultAsync` takes a query transform.** The argument is
  `Func<IQueryable<TEntity>, IQueryable<TResult>>`, so pass `q => q.Where(...)`, not a bare boolean
  predicate.
- **Returning `Handle` unintentionally.** `Handle` tells the repository to skip its own `Update`.
  Returning `Handle` without persisting silently drops the update.
- **Advisor bypassed.** The advisor chain runs only through `IRepository<TEntity>.UpdateAsync`.
  Mutating the EF Core context directly skips it.

## See also

- [Advice Pipeline](../documents/core/advice-pipeline.md) — `IAdvisor`, `AdviceContext`, `AdviseResult`
- [Mutation Pipeline](../documents/repository/mutation-pipeline.md) — built-in advisor order
- [Entity Traits](../documents/entity/traits.md) — trait interfaces such as `ICanonicalName`
