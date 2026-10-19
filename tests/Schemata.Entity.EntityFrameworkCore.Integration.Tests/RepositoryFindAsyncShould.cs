using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;
using Xunit;

namespace Schemata.Entity.EntityFrameworkCore.Integration.Tests;

[Trait("Category", "Integration")]
public class RepositoryFindAsyncShould : IAsyncLifetime
{
    private readonly string           _dbPath = $"{Guid.NewGuid():n}.db";
    private          ServiceProvider? _root;

    #region IAsyncLifetime Members

    public async Task InitializeAsync() {
        var services = new ServiceCollection();
        services.AddDbContextFactory<NullableKeyDbContext>(options => options.UseSqlite($"Data Source={_dbPath}"));
        services.AddScoped<IUnitOfWork<NullableKeyDbContext>, EfCoreUnitOfWork<NullableKeyDbContext>>();
        services.AddRepository<NullableKeyThing, EfCoreRepository<NullableKeyDbContext, NullableKeyThing>>();

        _root = services.BuildServiceProvider();

        using var scope = _root.CreateScope();
        var       db    = scope.ServiceProvider.GetRequiredService<NullableKeyDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() {
        if (_root is not null) {
            using (var scope = _root.CreateScope()) {
                var db = scope.ServiceProvider.GetRequiredService<NullableKeyDbContext>();
                await db.Database.EnsureDeletedAsync();
            }

            await _root.DisposeAsync();
        }

        if (File.Exists(_dbPath)) {
            File.Delete(_dbPath);
        }
    }

    #endregion

    [Fact]
    public async Task FindAsync_NullableKey_MatchesRowByValue() {
        {
            using var scope      = _root!.CreateScope();
            var       repository = scope.ServiceProvider.GetRequiredService<IRepository<NullableKeyThing>>();

            await repository.AddAsync(new NullableKeyThing { Id = 42, Name = "answer" });
            await repository.CommitAsync();
        }

        {
            using var scope      = _root!.CreateScope();
            var       repository = scope.ServiceProvider.GetRequiredService<IRepository<NullableKeyThing>>();

            var found = await repository.FindAsync([42L]);

            Assert.NotNull(found);
            Assert.Equal("answer", found.Name);
        }
    }

    #region Nested type: NullableKeyThing

    [Schemata.Abstractions.Entities.PrimaryKey(nameof(NullableKeyThing.Id))]
    public sealed class NullableKeyThing
    {
        public long?   Id   { get; set; }
        public string? Name { get; set; }
    }

    #endregion

    #region Nested type: NullableKeyDbContext

    public sealed class NullableKeyDbContext : DbContext
    {
        public NullableKeyDbContext(DbContextOptions<NullableKeyDbContext> options) : base(options) { }

        public DbSet<NullableKeyThing> Things { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder) {
            modelBuilder.Entity<NullableKeyThing>().HasKey(t => t.Id);
            modelBuilder.Entity<NullableKeyThing>().Property(t => t.Id).ValueGeneratedNever();
        }
    }

    #endregion
}
