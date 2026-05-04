using System;
using System.IO;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.Mapping;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Entity.Repository;
using Xunit;

namespace Schemata.Entity.LinqToDB.Integration.Tests.Fixtures;

public class IntegrationFixture : IAsyncLifetime
{
    private readonly string _dbPath = $"{Guid.NewGuid():N}.db";

    private ServiceProvider? _root;

    public IServiceProvider ServiceProvider => _root!;

    #region IAsyncLifetime Members

    public Task InitializeAsync() {
        MappingSchema.Default.AddMetadataReader(new SystemComponentModelDataAnnotationsSchemaAttributeReader());

        var services = new ServiceCollection();

        var connectionString = $"Data Source={_dbPath}";

        services.TryAddScoped(_ => {
                var options = new DataOptions().UseSQLite(connectionString);
                return new TestDataConnection(options);
            }
        );

        services.AddRepository<Student, LinQ2DbRepository<TestDataConnection, Student>>();
        services.AddRepository<Course, LinQ2DbRepository<TestDataConnection, Course>>();

        services.AddScoped<IUnitOfWork<TestDataConnection>, LinqToDbUnitOfWork<TestDataConnection>>();

        _root = services.BuildServiceProvider();

        // Create the tables
        using var scope      = _root.CreateScope();
        var       connection = scope.ServiceProvider.GetRequiredService<TestDataConnection>();
        connection.CreateTable<Student>(tableOptions: TableOptions.CreateIfNotExists);
        connection.CreateTable<Course>(tableOptions: TableOptions.CreateIfNotExists);

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

    public (IRepository<Student> StudentRepo, IRepository<Course> CourseRepo, IUnitOfWork<TestDataConnection> Uow, IServiceScope Scope) CreateScopeWithUoW() {
        var scope       = _root!.CreateScope();
        var studentRepo = scope.ServiceProvider.GetRequiredService<IRepository<Student>>();
        var courseRepo  = scope.ServiceProvider.GetRequiredService<IRepository<Course>>();
        var uow         = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDataConnection>>();
        return (studentRepo, courseRepo, uow, scope);
    }
}
