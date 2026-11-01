using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Entity.Repository;
using Xunit;

namespace Schemata.Entity.EntityFrameworkCore.Integration.Tests;

/// <summary>
///     Pins the deliberate boundary: <see cref="ResourceReferenceAttribute" /> is declarative
///     metadata. The framework does not validate reference values at write time; an application
///     that needs type or existence checks supplies them in its own advisor or business logic.
/// </summary>
[Trait("Layer", "Integration")]
public class LogicalReferenceShould
{
    [Fact]
    public async Task Write_Persists_Unresolved_References_Without_Framework_Validation() {
        using var database = new SqliteConnection("Data Source=:memory:");
        await database.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContextFactory<Database>(options => options.UseSqlite(database).ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        services.AddRepository<Target, EfCoreRepository<Database, Target>>();
        services.AddRepository<Source, EfCoreRepository<Database, Source>>();
        await using var root = services.BuildServiceProvider();
        await using (var setup = root.CreateAsyncScope()) {
            await setup.ServiceProvider.GetRequiredService<Database>().Database.EnsureCreatedAsync();
        }

        await using (var scope = root.CreateAsyncScope()) {
            var sources = scope.ServiceProvider.GetRequiredService<IRepository<Source>>();
            await sources.AddAsync(new() {
                Name        = "one",
                Typed       = "targets/missing",
                Polymorphic = "https://issuer.example",
                TypeOnly    = "targets/missing",
            });
            await sources.AddAsync(new() { Name = "two", Typed = "targets/missing" });
            await sources.CommitAsync();
        }

        await using var verify = root.CreateAsyncScope();
        var databaseContext = verify.ServiceProvider.GetRequiredService<Database>();
        Assert.Equal(2, await databaseContext.Set<Source>().CountAsync());
        Assert.Equal(0, await databaseContext.Set<Target>().CountAsync());
    }

    [CanonicalName("targets/{target}")]
    [Schemata.Abstractions.Entities.PrimaryKey(nameof(Uid))]
    public sealed class Target : IIdentifier, ICanonicalName
    {
        public Guid    Uid           { get; set; }
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    [CanonicalName("sources/{source}")]
    [Schemata.Abstractions.Entities.PrimaryKey(nameof(Uid))]
    public sealed class Source : IIdentifier, ICanonicalName
    {
        public Guid    Uid           { get; set; }
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }

        [ResourceReference(typeof(Target))] public string? Typed       { get; set; }
        [ResourceReference]                 public string? Polymorphic { get; set; }
        [ResourceReference(typeof(Target))] public string? TypeOnly    { get; set; }
    }

    public sealed class Database(DbContextOptions<Database> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model) {
            model.Entity<Target>();
            model.Entity<Source>();
            base.OnModelCreating(model);
        }
    }
}
