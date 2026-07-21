using System;
using System.IO;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.Mapping;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Caching.Distributed;
using Schemata.Caching.Skeleton;
using Schemata.Common;
using Schemata.Entity.Repository;
using Xunit;

namespace Schemata.Entity.LinqToDB.Integration.Tests.Fixtures;

public class IntegrationFixture : IAsyncLifetime
{
    private readonly string _dbPath = $"{Identifiers.NewUid():n}.db";
    private readonly bool _useQueryCache;

    private ServiceProvider? _root;

    public IServiceProvider ServiceProvider => _root!;

    public IntegrationFixture(bool useQueryCache = false) {
        _useQueryCache = useQueryCache;
    }

    #region IAsyncLifetime Members

    public Task InitializeAsync() {
        // A fixture-private schema keeps parallel test classes from mutating
        // MappingSchema.Default concurrently, which invalidates linq2db's
        // global entity-descriptor caches mid-flight.
        var schema = new MappingSchema();
        schema.AddMetadataReader(new SystemComponentModelDataAnnotationsSchemaAttributeReader());

        var services = new ServiceCollection();

        var connectionString = $"Data Source={_dbPath}";
        var options          = new DataOptions().UseSQLite(connectionString).UseMappingSchema(schema);

        services.TryAddScoped(_ => new TestDataConnection(options));

        services.TryAddSingleton<Func<TestDataConnection>>(sp => () => new(options));

        if (_useQueryCache) {
            services.AddDistributedMemoryCache();
            services.TryAddSingleton<ICacheProvider>(sp => new DistributedCacheProvider(sp.GetRequiredService<IDistributedCache>()));
        }

        var students = services.AddRepository<Student, LinqToDbRepository<TestDataConnection, Student>>();
        if (_useQueryCache) {
            students.UseQueryCache();
        }

        services.AddRepository<Course, LinqToDbRepository<TestDataConnection, Course>>();

        services.AddScoped<IUnitOfWork<TestDataConnection>, LinqToDbUnitOfWork<TestDataConnection>>();

        _root = services.BuildServiceProvider();

        using var scope      = _root.CreateScope();
        var       connection = scope.ServiceProvider.GetRequiredService<TestDataConnection>();
        connection.CreateTableWithIndexes<Student>();
        connection.CreateTableWithIndexes<Course>();

        return Task.CompletedTask;
    }

    public Task DisposeAsync() {
        _root?.Dispose();

        SqliteConnection.ClearAllPools();

        if (File.Exists(_dbPath)) {
            File.Delete(_dbPath);
        }

        return Task.CompletedTask;
    }

    #endregion

    public (IRepository<Student> Repository, IServiceScope Scope) CreateScopeWithRepository() {
        var scope      = _root!.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<Student>>();
        return (repository, scope);
    }

    public (IRepository<Course> Repository, IServiceScope Scope) CreateScopeWithCourseRepository() {
        var scope      = _root!.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<Course>>();
        return (repository, scope);
    }

    public (IRepository<Student> StudentRepo, IRepository<Course> CourseRepo, IUnitOfWork<TestDataConnection> Uow,
        IServiceScope Scope) CreateScopeWithUoW() {
        var scope       = _root!.CreateScope();
        var studentRepo = scope.ServiceProvider.GetRequiredService<IRepository<Student>>();
        var courseRepo  = scope.ServiceProvider.GetRequiredService<IRepository<Course>>();
        var uow         = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDataConnection>>();
        return (studentRepo, courseRepo, uow, scope);
    }
}
