using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Entity.Repository;
using Xunit;

namespace Schemata.Entity.EntityFrameworkCore.Integration.Tests.Fixtures;

public class IntegrationFixture : IAsyncLifetime
{
    private readonly string _dbPath = $"{Guid.NewGuid():N}.db";

    private ServiceProvider? _root;

    public IServiceProvider ServiceProvider => _root!;

    #region IAsyncLifetime Members

    public async Task InitializeAsync() {
        var services = new ServiceCollection();

        services.AddDbContext<TestDbContext>(opts => opts.UseSqlite($"Data Source={_dbPath}"));

        services.AddRepository<Student, EntityFrameworkCoreRepository<TestDbContext, Student>>();
        services.AddRepository<Course, EntityFrameworkCoreRepository<TestDbContext, Course>>();

        services.AddScoped<IUnitOfWork<TestDbContext>, EfCoreUnitOfWork<TestDbContext>>();

        _root = services.BuildServiceProvider();

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

    public (IRepository<Student> StudentRepo, IRepository<Course> CourseRepo, IUnitOfWork<TestDbContext> Uow, IServiceScope Scope) CreateScopeWithUoW() {
        var scope       = _root!.CreateScope();
        var studentRepo = scope.ServiceProvider.GetRequiredService<IRepository<Student>>();
        var courseRepo  = scope.ServiceProvider.GetRequiredService<IRepository<Course>>();
        var uow         = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        return (studentRepo, courseRepo, uow, scope);
    }
}
