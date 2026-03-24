using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mapster;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Mapping.Mapster;
using Schemata.Mapping.Skeleton;
using Schemata.Resource.Foundation.Advisors;
using Xunit;

namespace Schemata.Resource.Foundation.Integration.Tests.Fixtures;

/// <summary>
///     Integration fixture that wires a real ResourceOperationHandler with
///     a SQLite database (unique per fixture), a real EF Core repository, and Mapster.
///     A fresh database is created per fixture instance (per test class).
/// </summary>
public class IntegrationFixture : IAsyncLifetime
{
    private readonly string _dbPath = $"{Guid.NewGuid():N}.db";

    private ServiceProvider? _root;

    public IServiceProvider ServiceProvider => _root!;

    #region IAsyncLifetime Members

    public async Task InitializeAsync() {
        var services = new ServiceCollection();

        // DbContext with SQLite file (unique per fixture)
        services.AddDbContext<TestDbContext>(opts => opts.UseSqlite($"Data Source={_dbPath}"));

        // Register IRepository<Student> -> EntityFrameworkCoreRepository<TestDbContext, Student>
        // The closed-generic EF Core repo is registered directly; the EF repo ctor takes
        // (IServiceProvider, TContext) so ActivatorUtilities can resolve it from a scope.
        services.AddScoped<IRepository<Student>, EntityFrameworkCoreRepository<TestDbContext, Student>>();
        services.AddScoped<EntityFrameworkCoreRepository<TestDbContext, Student>>();

        // Standard repository advisors
        services.TryAddEnumerable(ServiceDescriptor.Scoped(
                                      typeof(IRepositoryBuildQueryAdvisor<>), typeof(AdviceBuildQuerySoftDelete<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(
                                      typeof(IRepositoryAddAdvisor<>), typeof(AdviceAddCanonicalName<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(
                                      typeof(IRepositoryAddAdvisor<>), typeof(AdviceAddConcurrency<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(
                                      typeof(IRepositoryAddAdvisor<>), typeof(AdviceAddSoftDelete<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(
                                      typeof(IRepositoryAddAdvisor<>), typeof(AdviceAddTimestamp<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(
                                      typeof(IRepositoryAddAdvisor<>), typeof(AdviceAddValidation<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(
                                      typeof(IRepositoryRemoveAdvisor<>), typeof(AdviceRemoveSoftDelete<>)));
        // Note: AdviceUpdateConcurrency is intentionally NOT registered.
        // It would call GetAsync<IConcurrency> which loads a second EF-tracked instance
        // with the same key as the entity being updated, causing EF to throw.
        // Timestamp-based concurrency is tested at the resource layer via AdviceUpdateFreshness.
        services.TryAddEnumerable(ServiceDescriptor.Scoped(
                                      typeof(IRepositoryUpdateAdvisor<>), typeof(AdviceUpdateTimestamp<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(
                                      typeof(IRepositoryUpdateAdvisor<>), typeof(AdviceUpdateValidation<>)));

        // Custom advisor: assign a GUID-based Name to every new Student so that
        // FindByNameAsync works in integration tests (the framework never auto-generates
        // Name from Id; it expects a caller-provided slug pre-set on the entity).
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<Student>, StudentNameAdvisor>());

        // Mapster: register TypeAdapterConfig and ISimpleMapper.
        // We use a fresh config (not GlobalSettings) with IgnoreNullValues so null
        // fields on a partial update request do not overwrite stored entity values.
        services.TryAddSingleton(_ => {
            var config = new TypeAdapterConfig();
            config.Default.IgnoreNullValues(true).PreserveReference(false);
            return config;
        });
        services.TryAddScoped<ISimpleMapper, SimpleMapper>();

        // Resource options — suppress validation noise; keep freshness enabled
        services.AddSingleton(Options.Create(new SchemataResourceOptions {
            SuppressCreateValidation = true, SuppressUpdateValidation = true, SuppressFreshness = false,
        }));

        // Resource advisors for freshness / ETag concurrency checks
        services.TryAddEnumerable(ServiceDescriptor.Scoped(
                                      typeof(IResourceUpdateAdvisor<,>), typeof(AdviceUpdateFreshness<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(
                                      typeof(IResourceDeleteAdvisor<>), typeof(AdviceDeleteFreshness<>)));

        _root = services.BuildServiceProvider();

        // Create DB schema
        using var scope = _root.CreateScope();
        var       db    = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() {
        if (_root != null) {
            using var scope = _root.CreateScope();
            var       db    = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await db.Database.EnsureDeletedAsync();
            await _root.DisposeAsync();
        }

        if (File.Exists(_dbPath)) {
            File.Delete(_dbPath);
        }
    }

    #endregion

    /// <summary>
    ///     Creates a handler backed by a fresh DI scope so each call gets its own
    ///     DbContext instance, avoiding EF change-tracker conflicts across test steps.
    ///     The caller is responsible for disposing the returned scope.
    /// </summary>
    public (ResourceOperationHandler<Student, Student, Student, Student> Handler, IServiceScope Scope)
        CreateHandlerWithScope() {
        var scope      = _root!.CreateScope();
        var sp         = scope.ServiceProvider;
        var repository = sp.GetRequiredService<IRepository<Student>>();
        var mapper     = sp.GetRequiredService<ISimpleMapper>();
        var handler    = new ResourceOperationHandler<Student, Student, Student, Student>(sp, repository, mapper);
        return (handler, scope);
    }

    #region Nested type: StudentNameAdvisor

    private sealed class StudentNameAdvisor : IRepositoryAddAdvisor<Student>
    {
        #region IRepositoryAddAdvisor<Student> Members

        public int Order    => 0;

        public Task<AdviseResult> AdviseAsync(
            AdviceContext        ctx,
            IRepository<Student> repository,
            Student              entity,
            CancellationToken    ct
        ) {
            if (string.IsNullOrWhiteSpace(entity.Name)) {
                entity.Name = $"students/{Guid.NewGuid():N}";
            }

            return Task.FromResult(AdviseResult.Continue);
        }

        #endregion
    }

    #endregion
}
