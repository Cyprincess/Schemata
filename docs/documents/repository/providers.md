# Providers

A repository provider is the concrete `RepositoryBase<TEntity>` implementation that translates the repository abstraction into actual database operations. Schemata ships two providers: one backed by Entity Framework Core and one backed by LINQ to DB. Both implement the same `IRepository<TEntity>` interface and participate in the same advisor pipelines, so application code is provider-agnostic.

## Registering a provider

Provider registration is a two-step process: register the repository implementation type with `AddRepository`, then configure the underlying data access library with a `UseXxx` call on the returned builder.

```csharp
// Entity Framework Core
services.AddRepository(typeof(EntityFrameworkCoreRepository<,>))
    .UseEntityFrameworkCore<AppDbContext>((sp, options) => {
        options.UseSqlServer(connectionString);
    });

// LINQ to DB
services.AddRepository(typeof(LinQ2DbRepository<,>))
    .UseLinqToDb((sp, options) => {
        return options.UseSQLite(connectionString);
    });
```

### AddRepository

`AddRepository` is an extension method on `IServiceCollection` defined in `Schemata.Entity.Repository`. It accepts the open generic repository implementation type and:

1. Validates that the type implements both `IRepository` and `IRepository<>`.
2. Registers the open generic `IRepository<>` with the provided implementation type as a scoped service.
3. Registers all built-in advisors: timestamp tracking, concurrency, validation, soft-delete filtering, canonical name generation.
4. Returns a `SchemataRepositoryBuilder` for fluent configuration of the provider and additional advisors (such as query caching).

The built-in advisors registered automatically are:

| Advisor                        | Interface                        | Purpose                                    |
| ------------------------------ | -------------------------------- | ------------------------------------------ |
| `AdviceBuildQuerySoftDelete<>` | `IRepositoryBuildQueryAdvisor<>` | Filters soft-deleted entities from queries |
| `AdviceAddCanonicalName<>`     | `IRepositoryAddAdvisor<>`        | Sets canonical name on insert              |
| `AdviceAddConcurrency<>`       | `IRepositoryAddAdvisor<>`        | Sets concurrency token on insert           |
| `AdviceAddSoftDelete<>`        | `IRepositoryAddAdvisor<>`        | Initializes soft-delete state on insert    |
| `AdviceAddTimestamp<>`         | `IRepositoryAddAdvisor<>`        | Sets created/modified timestamps on insert |
| `AdviceAddValidation<>`        | `IRepositoryAddAdvisor<>`        | Validates entity on insert                 |
| `AdviceRemoveSoftDelete<>`     | `IRepositoryRemoveAdvisor<>`     | Converts hard delete to soft delete        |
| `AdviceUpdateConcurrency<>`    | `IRepositoryUpdateAdvisor<>`     | Updates concurrency token on update        |
| `AdviceUpdateTimestamp<>`      | `IRepositoryUpdateAdvisor<>`     | Updates modified timestamp on update       |
| `AdviceUpdateValidation<>`     | `IRepositoryUpdateAdvisor<>`     | Validates entity on update                 |

## Entity Framework Core provider

| Package                               | Dependency                                                               | Targets                                                 |
| ------------------------------------- | ------------------------------------------------------------------------ | ------------------------------------------------------- |
| `Schemata.Entity.EntityFrameworkCore` | `Schemata.Entity.Repository`, `Microsoft.EntityFrameworkCore.Relational` | `netstandard2.0`, `netstandard2.1`, `net8.0`, `net10.0` |

### Repository class

`EntityFrameworkCoreRepository<TContext, TEntity>` extends `RepositoryBase<TEntity>` where `TContext` is a `DbContext`. It exposes the underlying `DbContext` as the `Context` property and the `DbSet<TEntity>` as the `DbSet` property.

**Query methods** (`ListAsync`, `FirstOrDefaultAsync`, `SingleOrDefaultAsync`, `AnyAsync`, `CountAsync`, `LongCountAsync`) build the query through the `BuildQueryAsync` pipeline, run query advisors, execute via EF Core's async LINQ methods, then run result advisors.

**Mutation methods** (`AddAsync`, `UpdateAsync`, `RemoveAsync`) run their respective advisor pipelines and then delegate to EF Core's `AddAsync`, `Update`, and `Remove` methods on the context.

**CommitAsync** calls `Context.SaveChangesAsync`, which persists all tracked changes in a single database transaction managed by EF Core.

**Detach** sets the entity's `EntityState` to `Detached`. The `UpdateAsync` method calls `Detach` before `Context.Update` to avoid tracking conflicts.

**SearchAsync** is not implemented and throws `NotImplementedException`.

### UseEntityFrameworkCore

`UseEntityFrameworkCore` is an extension method on `SchemataRepositoryBuilder`. It registers the `DbContext` into the DI container using `AddDbContext`.

```csharp
// Single type (service and implementation are the same)
.UseEntityFrameworkCore<AppDbContext>((sp, options) => {
    options.UseSqlServer(connectionString);
})

// Separate service and implementation types
.UseEntityFrameworkCore<IAppDbContext, AppDbContext>((sp, options) => {
    options.UseNpgsql(connectionString);
})
```

**Parameters:**

| Parameter         | Type                                                 | Default  | Description                                                                   |
| ----------------- | ---------------------------------------------------- | -------- | ----------------------------------------------------------------------------- |
| `configure`       | `Action<IServiceProvider, DbContextOptionsBuilder>?` | --       | Configures the DbContext options (database provider, connection string, etc.) |
| `contextLifetime` | `ServiceLifetime`                                    | `Scoped` | Lifetime for the DbContext registration                                       |
| `optionsLifetime` | `ServiceLifetime`                                    | `Scoped` | Lifetime for the DbContextOptions registration                                |

The `configure` callback receives the `IServiceProvider`, allowing resolution of other services (such as configuration or tenant context) when building options.

## LINQ to DB provider

| Package                    | Dependency                              | Targets                                                 |
| -------------------------- | --------------------------------------- | ------------------------------------------------------- |
| `Schemata.Entity.LinqToDB` | `Schemata.Entity.Repository`, `linq2db` | `netstandard2.0`, `netstandard2.1`, `net8.0`, `net10.0` |

### Repository class

`LinQ2DbRepository<TContext, TEntity>` extends `RepositoryBase<TEntity>` where `TContext` is a `DataConnection`. It exposes the underlying `DataConnection` as the `Context` property and the `ITable<TEntity>` as the `Table` property.

**Table name resolution.** The table name is determined in the constructor: it first checks for a `System.ComponentModel.DataAnnotations.Schema.TableAttribute` on the entity type, and falls back to the pluralized entity type name (using Humanizer).

**Query methods** follow the same advisor pipeline pattern as the EF Core provider: build query, run query advisors, execute via LINQ to DB's async methods, run result advisors.

**Mutation methods** (`AddAsync`, `UpdateAsync`, `RemoveAsync`) each begin a transaction automatically if one is not already active, run their advisor pipelines, then execute `InsertAsync`, `UpdateAsync`, or `DeleteAsync` on the `DataConnection`. Each operation accumulates the number of affected rows in `RowsAffected`.

**CommitAsync** commits the active transaction and returns the cumulative `RowsAffected` count. If the commit fails, it rolls back the transaction and throws a `TransactionAbortedException`. After commit, the transaction and row counter are reset.

**Detach** is a no-op because LINQ to DB does not track entity state.

**SearchAsync** is not implemented and throws `NotImplementedException`.

### DataAnnotations metadata reader

LINQ to DB uses its own mapping attribute system. The `SystemComponentModelDataAnnotationsSchemaAttributeReader` bridges the gap by translating standard `System.ComponentModel.DataAnnotations` and `System.ComponentModel.DataAnnotations.Schema` attributes into LINQ to DB equivalents:

| DataAnnotations attribute               | LINQ to DB mapping                       |
| --------------------------------------- | ---------------------------------------- |
| `TableAttribute`                        | `TableAttribute` (with Name and Schema)  |
| `ColumnAttribute`                       | `ColumnAttribute` (with Name and DbType) |
| `NotMappedAttribute`                    | `NotColumnAttribute`                     |
| `KeyAttribute`                          | `PrimaryKeyAttribute`                    |
| `DatabaseGeneratedAttribute` (Identity) | `IdentityAttribute`                      |

This reader is registered on `MappingSchema.Default` when `UseLinqToDb` is called, so entities can use standard DataAnnotations attributes without adding LINQ to DB-specific annotations.

### UseLinqToDb

`UseLinqToDb` is an extension method on `SchemataRepositoryBuilder` with three overloads:

```csharp
// Default DataConnection
.UseLinqToDb((sp, options) => options.UseSQLite(connectionString))

// Custom DataConnection type (single type)
.UseLinqToDb<MyDataConnection>((sp, options) => options.UseSQLite(connectionString))

// Separate service and implementation types
.UseLinqToDb<IMyDataConnection, MyDataConnection>((sp, options) => options.UseSQLite(connectionString))
```

**Parameters:**

| Parameter         | Type                                               | Default  | Description                                                             |
| ----------------- | -------------------------------------------------- | -------- | ----------------------------------------------------------------------- |
| `configure`       | `Func<IServiceProvider, DataOptions, DataOptions>` | --       | Configures the DataOptions (database provider, connection string, etc.) |
| `contextLifetime` | `ServiceLifetime`                                  | `Scoped` | Lifetime for the DataConnection registration                            |
| `optionsLifetime` | `ServiceLifetime`                                  | `Scoped` | Lifetime for the DataOptions registration                               |

The three-type-parameter overload performs constructor detection on the implementation type. It looks for a constructor accepting `DataOptions<TContextImplementation>`, then `DataOptions<TContext>`, then plain `DataOptions`, and registers the appropriate options type in DI. If no matching constructor is found, it throws an `ArgumentException`.

## Differences between providers

| Aspect                       | EntityFrameworkCore                                                              | LinqToDB                                                                                                              |
| ---------------------------- | -------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------- |
| **Underlying library**       | `Microsoft.EntityFrameworkCore.Relational`                                       | `linq2db`                                                                                                             |
| **Context type**             | `DbContext`                                                                      | `DataConnection`                                                                                                      |
| **Change tracking**          | Full EF Core change tracking; `Detach` sets state to `EntityState.Detached`      | No change tracking; `Detach` is a no-op                                                                               |
| **Transaction management**   | Implicit via `SaveChangesAsync` -- all pending changes are committed together    | Explicit per-repository: a transaction is started on the first mutation and committed in `CommitAsync`                |
| **CommitAsync return value** | Number of state entries written (from `SaveChangesAsync`)                        | Cumulative rows affected across all mutations since the last commit                                                   |
| **Table name resolution**    | Driven by EF Core model configuration                                            | `TableAttribute` or pluralized entity type name                                                                       |
| **DataAnnotations support**  | Native                                                                           | Via `SystemComponentModelDataAnnotationsSchemaAttributeReader` metadata bridge                                        |
| **Mutation style**           | Tracked: `AddAsync` / `Update` / `Remove` on DbContext, flushed on `SaveChanges` | Immediate: `InsertAsync` / `UpdateAsync` / `DeleteAsync` execute SQL within a transaction, committed on `CommitAsync` |
| **UpdateAsync behavior**     | Calls `Detach` then `Context.Update` to avoid tracking conflicts                 | Calls `Context.UpdateAsync` directly                                                                                  |
| **Configure callback**       | `Action<IServiceProvider, DbContextOptionsBuilder>?` (void)                      | `Func<IServiceProvider, DataOptions, DataOptions>` (returns options)                                                  |
