using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Entity.Repository;
using Xunit;

namespace Schemata.Entity.EntityFrameworkCore.Integration.Tests;

[Trait("Layer", "Integration")]
public class LogicalReferenceShould
{
    [Fact]
    public async Task Validate_Opted_In_References_Without_Owner_And_Allow_Many_To_One() {
        using var database = new SqliteConnection("Data Source=:memory:");
        await database.OpenAsync();
        var resolver = new Mock<IResourceTypeResolver>();
        resolver.Setup(value => value.Resolve(It.IsAny<string>())).Returns(typeof(Target));
        var services = new ServiceCollection();
        services.AddSingleton(resolver.Object);
        services.AddDbContextFactory<Database>(options => options.UseSqlite(database).ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        services.AddRepository<Target, EfCoreRepository<Database, Target>>();
        services.AddRepository<Source, EfCoreRepository<Database, Source>>();
        await using var root = services.BuildServiceProvider();
        await using (var setup = root.CreateAsyncScope()) {
            await setup.ServiceProvider.GetRequiredService<Database>().Database.EnsureCreatedAsync();
            var targets = setup.ServiceProvider.GetRequiredService<IRepository<Target>>();
            await targets.AddAsync(new() { Name = "present" });
            await targets.CommitAsync();
        }
        await using (var scope = root.CreateAsyncScope()) {
            var sources = scope.ServiceProvider.GetRequiredService<IRepository<Source>>();
            await sources.AddAsync(new() { Name = "one", Typed = "targets/present", Polymorphic = "targets/present", TypeOnly = "targets/missing" });
            await sources.AddAsync(new() { Name = "two", Typed = "targets/present" });
            await sources.CommitAsync();
        }
        await using (var missing = root.CreateAsyncScope()) {
            var sources = missing.ServiceProvider.GetRequiredService<IRepository<Source>>();
            await Assert.ThrowsAsync<NotFoundException>(() => sources.AddAsync(new() { Name = "three", Typed = "targets/missing" }));
        }
        await using var verify = root.CreateAsyncScope();
        Assert.Equal(2, await verify.ServiceProvider.GetRequiredService<Database>().Set<Source>().CountAsync());
    }

    [CanonicalName("targets/{target}")]
    [Schemata.Abstractions.Entities.PrimaryKey(nameof(Uid))]
    public sealed class Target : IIdentifier, ICanonicalName
    {
        public Guid Uid { get; set; }
        public string? Name { get; set; }
        public string? CanonicalName { get; set; }
    }
    [CanonicalName("sources/{source}")]
    [Schemata.Abstractions.Entities.PrimaryKey(nameof(Uid))]
    public sealed class Source : IIdentifier, ICanonicalName
    {
        public Guid Uid { get; set; }
        public string? Name { get; set; }
        public string? CanonicalName { get; set; }
        [ResourceReference(typeof(Target), ValidateExistence = true)] public string? Typed { get; set; }
        [ResourceReference(ValidateExistence = true)] public string? Polymorphic { get; set; }
        [ResourceReference(typeof(Target))] public string? TypeOnly { get; set; }
    }
    public sealed class Database(DbContextOptions<Database> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model) { model.Entity<Target>(); model.Entity<Source>(); base.OnModelCreating(model); }
    }
}
