using System;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Entity.EntityFrameworkCore.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Entity.EntityFrameworkCore.Integration.Tests;

[Trait("Category", "Integration")]
public class QueryCacheKeyShould : IDisposable
{
    private readonly string         _dbPath     = $"{Guid.NewGuid():n}.db";
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider  _root;
    private readonly EfCoreRepository<TestDbContext, Student> _repository;
    private readonly TestDbContext    _context;

    public QueryCacheKeyShould() {
        _connection = new($"Data Source={_dbPath}");

        var services = new ServiceCollection();
        services.AddDbContextFactory<TestDbContext>(options => options.UseSqlite(_connection)
                                                                       .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        _root = services.BuildServiceProvider();

        var factory = _root.GetRequiredService<IDbContextFactory<TestDbContext>>();

        _repository = new(_root, factory);
        _context    = factory.CreateDbContext();

        _connection.Open();
        _context.Database.EnsureCreated();
    }

    [Fact]
    public void Key_Is_Stable_And_Distinguishes_Predicates() {
        var keyed = _context.Students.Where(student => student.Age > 20);

        var key = _repository.GetQueryCacheKey(keyed);

        Assert.NotNull(key);
        Assert.Equal(key, _repository.GetQueryCacheKey(keyed));
        Assert.NotEqual(key, _repository.GetQueryCacheKey(_context.Students.Where(student => student.Age > 21)));
    }

    [Fact]
    public void Key_Is_Null_For_InMemory_Sources() {
        foreach (var connectionString in new[] { "Data Source=:memory:", "Data Source=namedmem;Mode=Memory" }) {
            var services = new ServiceCollection();
            services.AddDbContextFactory<TestDbContext>(options => options.UseSqlite(connectionString)
                .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
            using var root = services.BuildServiceProvider();
            var factory = root.GetRequiredService<IDbContextFactory<TestDbContext>>();
            using var repository = new EfCoreRepository<TestDbContext, Student>(root, factory);
            Assert.Null(repository.GetQueryCacheKey(Enumerable.Empty<Student>().AsQueryable()));
        }
    }

    public void Dispose() {
        _repository.Dispose();
        _context.Dispose();
        _connection.Dispose();
        SqliteConnection.ClearAllPools();
        _root.Dispose();
        File.Delete(_dbPath);

        GC.SuppressFinalize(this);
    }
}
