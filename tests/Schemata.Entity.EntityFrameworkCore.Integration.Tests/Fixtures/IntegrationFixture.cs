using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Entity.Repository;
using Xunit;

namespace Schemata.Entity.EntityFrameworkCore.Integration.Tests.Fixtures;

public class IntegrationFixture : IAsyncLifetime
{
    private readonly string _dbPath = $"{Guid.NewGuid():n}.db";
    private readonly bool _useQueryCache;

    private ServiceProvider? _root;

    public IServiceProvider ServiceProvider => _root!;

    public IntegrationFixture(bool useQueryCache = false) {
        _useQueryCache = useQueryCache;
    }

    #region IAsyncLifetime Members

    public async Task InitializeAsync() {
        var services = new ServiceCollection();

        services.AddDbContextFactory<TestDbContext>(opts => opts.UseSqlite($"Data Source={_dbPath}")
                                                         .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());

        var students = services.AddRepository<Student, EfCoreRepository<TestDbContext, Student>>();
        if (_useQueryCache) {
            services.AddMemoryCacheProvider();
            students.UseQueryCache();
        }
        services.AddRepository<Course, EfCoreRepository<TestDbContext, Course>>();
        services.AddRepository<NestedThing, EfCoreRepository<TestDbContext, NestedThing>>();
        services.AddRepository<StampedNestedThing, EfCoreRepository<TestDbContext, StampedNestedThing>>();

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

    public (IRepository<Student> StudentRepo, IRepository<Course> CourseRepo, IUnitOfWork<TestDbContext> Uow,
        IServiceScope Scope) CreateScopeWithUoW() {
        var scope       = _root!.CreateScope();
        var studentRepo = scope.ServiceProvider.GetRequiredService<IRepository<Student>>();
        var courseRepo  = scope.ServiceProvider.GetRequiredService<IRepository<Course>>();
        var uow         = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
        return (studentRepo, courseRepo, uow, scope);
    }
}
