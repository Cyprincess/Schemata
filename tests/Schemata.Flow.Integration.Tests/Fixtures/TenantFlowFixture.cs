using System;
using System.IO;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.LinqToDB;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton;
using Schemata.Tenancy.Foundation;
using Schemata.Tenancy.Foundation.Services;
using Schemata.Tenancy.Skeleton.Entities;
using Xunit;

namespace Schemata.Flow.Integration.Tests.Fixtures;

/// <summary>
///     Hosts the Flow persistence graph on host-defined <see cref="TenantProcess" /> types so the
///     tenant isolation guarantees are proven against the entity set a host actually registers,
///     not the built-in defaults.
/// </summary>
public sealed class TenantFlowFixture(bool linq) : IAsyncLifetime, IFlowIntegrationFixture
{
    private readonly string _dbPath = $"{Guid.NewGuid():n}.db";

    private ServiceProvider?                                   _root;
    private MemoryCacheTenantProviderCache?                    _cache;
    private SchemataTenantServiceScopeFactory<SchemataTenant>? _scopes;

    public Action<IServiceCollection>? ConfigureServices { get; init; }

    #region IAsyncLifetime Members

    public async Task InitializeAsync() {
        var services = new ServiceCollection();

        if (linq) {
            var schema = new MappingSchema();
            schema.AddMetadataReader(new SystemComponentModelDataAnnotationsSchemaAttributeReader());
            var options = new DataOptions().UseSQLite($"Data Source={_dbPath}").UseMappingSchema(schema);
            FlowFixtureServices.AddTenantFlowRepositories(services, options);
            FlowFixtureServices.AddTenantRepositories<TenantProcess, TenantProcessToken, TenantProcessTransition,
                TenantProcessCompensation, TenantProcessParticipant, TenantProcessSource>(services, builder => {
                AddSharedTable<TenantProcess, LinqToDbRepository<TestDataConnection, TenantProcess>>(builder.Services,
                    tenant => tenant.TryAddSingleton<Func<TestDataConnection>>(_ => () => new(options)));
                AddSharedTable<TenantProcessToken, LinqToDbRepository<TestDataConnection, TenantProcessToken>>(builder.Services,
                    tenant => tenant.TryAddSingleton<Func<TestDataConnection>>(_ => () => new(options)));
                AddSharedTable<TenantProcessTransition, LinqToDbRepository<TestDataConnection, TenantProcessTransition>>(builder.Services,
                    tenant => tenant.TryAddSingleton<Func<TestDataConnection>>(_ => () => new(options)));
                AddSharedTable<TenantProcessCompensation, LinqToDbRepository<TestDataConnection, TenantProcessCompensation>>(builder.Services,
                    tenant => tenant.TryAddSingleton<Func<TestDataConnection>>(_ => () => new(options)));
                AddSharedTable<TenantProcessParticipant, LinqToDbRepository<TestDataConnection, TenantProcessParticipant>>(builder.Services,
                    tenant => tenant.TryAddSingleton<Func<TestDataConnection>>(_ => () => new(options)));
                AddSharedTable<TenantProcessSource, LinqToDbRepository<TestDataConnection, TenantProcessSource>>(builder.Services,
                    tenant => tenant.TryAddSingleton<Func<TestDataConnection>>(_ => () => new(options)));
                AddSharedTable<TenantFlowEffectIntent, LinqToDbRepository<TestDataConnection, TenantFlowEffectIntent>>(builder.Services,
                    tenant => tenant.TryAddSingleton<Func<TestDataConnection>>(_ => () => new(options)));
            });
        } else {
            FlowFixtureServices.AddTenantFlowRepositories(services, _dbPath);
            FlowFixtureServices.AddTenantRepositories<TenantProcess, TenantProcessToken, TenantProcessTransition,
                TenantProcessCompensation, TenantProcessParticipant, TenantProcessSource>(services, builder => {
                AddSharedTable<TenantProcess, EfCoreRepository<TenantFlowDbContext, TenantProcess>>(builder.Services,
                    tenant => tenant.AddRepositorySource<TenantFlowDbContext>((_, options) => options.UseSqlite($"Data Source={_dbPath}")));
                AddSharedTable<TenantProcessToken, EfCoreRepository<TenantFlowDbContext, TenantProcessToken>>(builder.Services,
                    tenant => tenant.AddRepositorySource<TenantFlowDbContext>((_, options) => options.UseSqlite($"Data Source={_dbPath}")));
                AddSharedTable<TenantProcessTransition, EfCoreRepository<TenantFlowDbContext, TenantProcessTransition>>(builder.Services,
                    tenant => tenant.AddRepositorySource<TenantFlowDbContext>((_, options) => options.UseSqlite($"Data Source={_dbPath}")));
                AddSharedTable<TenantProcessCompensation, EfCoreRepository<TenantFlowDbContext, TenantProcessCompensation>>(builder.Services,
                    tenant => tenant.AddRepositorySource<TenantFlowDbContext>((_, options) => options.UseSqlite($"Data Source={_dbPath}")));
                AddSharedTable<TenantProcessParticipant, EfCoreRepository<TenantFlowDbContext, TenantProcessParticipant>>(builder.Services,
                    tenant => tenant.AddRepositorySource<TenantFlowDbContext>((_, options) => options.UseSqlite($"Data Source={_dbPath}")));
                AddSharedTable<TenantProcessSource, EfCoreRepository<TenantFlowDbContext, TenantProcessSource>>(builder.Services,
                    tenant => tenant.AddRepositorySource<TenantFlowDbContext>((_, options) => options.UseSqlite($"Data Source={_dbPath}")));
                AddSharedTable<TenantFlowEffectIntent, EfCoreRepository<TenantFlowDbContext, TenantFlowEffectIntent>>(builder.Services,
                    tenant => tenant.AddRepositorySource<TenantFlowDbContext>((_, options) => options.UseSqlite($"Data Source={_dbPath}")));
            });
        }

        FlowFixtureServices.AddFlowServices<TenantProcess, TenantProcessToken, TenantProcessTransition,
            TenantProcessCompensation, TenantProcessParticipant, TenantProcessSource, TenantFlowEffectIntent>(services);
        services.AddSingleton<IOptions<SchemataFlowOptions>>(Options.Create(new SchemataFlowOptions()));
        ConfigureServices?.Invoke(services);

        _root = services.BuildServiceProvider();
        (_cache, _scopes) = FlowFixtureServices.CreateTenantScopes(_root);

        using (var scope = _root.CreateScope()) {
            if (linq) {
                var connection = scope.ServiceProvider.GetRequiredService<TestDataConnection>();
                connection.CreateTable<TenantProcess>(tableOptions: TableOptions.CreateIfNotExists);
                connection.Execute("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_TenantFlowProcesses_Tenant_Idempotency\" ON \"TenantFlowProcesses\" (\"TenantUid\", \"DefinitionName\", \"IdempotencyKey\") WHERE \"TenantUid\" IS NOT NULL");
                connection.Execute("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_TenantFlowProcesses_Host_Idempotency\" ON \"TenantFlowProcesses\" (\"DefinitionName\", \"IdempotencyKey\") WHERE \"TenantUid\" IS NULL");
                connection.CreateTable<TenantProcessToken>(tableOptions: TableOptions.CreateIfNotExists);
                connection.CreateTable<TenantProcessTransition>(tableOptions: TableOptions.CreateIfNotExists);
                connection.CreateTable<TenantProcessSource>(tableOptions: TableOptions.CreateIfNotExists);
                connection.Execute("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_TenantFlowProcessSources_Tenant_Token\" ON \"TenantFlowProcessSources\" (\"TenantUid\", \"Process\", \"Token\", \"Name\") WHERE \"TenantUid\" IS NOT NULL AND \"Token\" IS NOT NULL");
                connection.Execute("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_TenantFlowProcessSources_Tenant_Process\" ON \"TenantFlowProcessSources\" (\"TenantUid\", \"Process\", \"Name\") WHERE \"TenantUid\" IS NOT NULL AND \"Token\" IS NULL");
                connection.Execute("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_TenantFlowProcessSources_Host_Token\" ON \"TenantFlowProcessSources\" (\"Process\", \"Token\", \"Name\") WHERE \"TenantUid\" IS NULL AND \"Token\" IS NOT NULL");
                connection.Execute("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_TenantFlowProcessSources_Host_Process\" ON \"TenantFlowProcessSources\" (\"Process\", \"Name\") WHERE \"TenantUid\" IS NULL AND \"Token\" IS NULL");
                connection.CreateTable<TenantProcessCompensation>(tableOptions: TableOptions.CreateIfNotExists);
                connection.CreateTable<TenantProcessParticipant>(tableOptions: TableOptions.CreateIfNotExists);
                connection.CreateTable<TenantFlowEffectIntent>(tableOptions: TableOptions.CreateIfNotExists);
                connection.Execute("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_TenantFlowEffectIntents_Tenant_RequestId\" ON \"TenantFlowEffectIntents\" (\"TenantUid\", \"RequestId\") WHERE \"TenantUid\" IS NOT NULL");
                connection.Execute("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_TenantFlowEffectIntents_Host_RequestId\" ON \"TenantFlowEffectIntents\" (\"RequestId\") WHERE \"TenantUid\" IS NULL");
            } else {
                var db = scope.ServiceProvider.GetRequiredService<TenantFlowDbContext>();
                await db.Database.EnsureCreatedAsync();
            }
        }
    }

    public async Task DisposeAsync() {
        if (_root is not null) {
            if (!linq) {
                using var scope = _root.CreateScope();
                var       db    = scope.ServiceProvider.GetRequiredService<TenantFlowDbContext>();
                await db.Database.EnsureDeletedAsync();
            }

            await _cache!.DisposeAsync();
            await _root.DisposeAsync();
        }

        if (File.Exists(_dbPath)) {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            SqliteConnection.ClearPool(connection);
            File.Delete(_dbPath);
        }
    }

    #endregion

    public IServiceScope CreateScope() { return _root!.CreateScope(); }

    public ValueTask<AsyncServiceScope> EnterTenantAsync(Guid? tenant) {
        return _scopes!.CreateAsync(new(tenant));
    }

    private static void AddSharedTable<TEntity, TRepository>(
        IServiceCollection      services,
        Action<IServiceCollection> configureSource
    )
        where TEntity : class
        where TRepository : class, IRepository<TEntity> {
        services.AddTransient<IRepository<TEntity>, TRepository>();
        services.AddScoped<IResourceMutation<TEntity>, ResourceMutation<TEntity>>();
        configureSource(services);
    }
}
