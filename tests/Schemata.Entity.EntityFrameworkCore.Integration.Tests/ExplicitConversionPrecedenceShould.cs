using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Entity.EntityFrameworkCore.Conversions;
using Xunit;

namespace Schemata.Entity.EntityFrameworkCore.Integration.Tests;

/// <summary>
///     Behavioral coverage for explicit-conversion precedence over the automatic JSON
///     conventions, per issue #206: a consumer-configured converter (and its comparer)
///     survives the framework customizer on every JSON-eligible shape, while properties
///     without one keep the automatic JSON conversion.
/// </summary>
[Trait("Category", "Integration")]
public class ExplicitConversionPrecedenceShould : IAsyncLifetime
{
    private readonly string           _dbPath = $"{Guid.NewGuid():n}.db";
    private          ServiceProvider? _root;

    #region IAsyncLifetime Members

    public async Task InitializeAsync() {
        var services = new ServiceCollection();
        services.AddDbContextFactory<ConversionDbContext>(options => {
            options.UseSqlite($"Data Source={_dbPath}");
            options.ReplaceService<IModelCustomizer, SchemataModelCustomizer>();
        });
        _root = services.BuildServiceProvider();

        using var scope = _root.CreateScope();
        var       db    = scope.ServiceProvider.GetRequiredService<ConversionDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() {
        if (_root is not null) {
            using (var scope = _root.CreateScope()) {
                var db = scope.ServiceProvider.GetRequiredService<ConversionDbContext>();
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
    public async Task ConsumerConversion_OnAutomaticShape_IsPreservedWithItsComparer() {
        Guid uid;
        {
            using var scope = _root!.CreateScope();
            var       db    = scope.ServiceProvider.GetRequiredService<ConversionDbContext>();

            var ledger = new Ledger {
                Uid  = Guid.NewGuid(),
                Tags = new() { ["b"] = "2", ["a"] = "1" },
            };
            db.Ledgers.Add(ledger);
            await db.SaveChangesAsync();
            uid = ledger.Uid;
        }

        {
            using var scope = _root!.CreateScope();
            var       db    = scope.ServiceProvider.GetRequiredService<ConversionDbContext>();

            var property = db.Model.FindEntityType(typeof(Ledger))!.FindProperty(nameof(Ledger.Tags))!;
            Assert.NotNull(property.GetValueConverter());
            Assert.IsNotType<EfCoreJsonValueConverter<Dictionary<string, string?>>>(property.GetValueConverter());

            var comparer = property.GetValueComparer();
            Assert.NotNull(comparer);

            var tags = new Dictionary<string, string?> { ["a"] = "1", ["b"] = "2" };
            Assert.Equal(2, comparer.GetHashCode(tags));

            var found = await db.Ledgers.FindAsync(uid);
            Assert.NotNull(found);
            Assert.NotNull(found.Tags);
            Assert.Equal("1", found.Tags!["a"]);
            Assert.Equal("2", found.Tags!["b"]);

            // The consumer representation is what reached the store.
            var raw = await db.Database.SqlQuery<string>($"SELECT Tags AS \"Value\" FROM Ledgers WHERE Uid = {uid}").SingleAsync();
            Assert.Equal("a=1;b=2", raw);
        }
    }

    [Fact]
    public void AutomaticJsonConversion_StillApplies_WithoutConsumerConversion() {
        using var scope = _root!.CreateScope();
        var       db    = scope.ServiceProvider.GetRequiredService<ConversionDbContext>();

        var labels = db.Model.FindEntityType(typeof(Ledger))!.FindProperty(nameof(Ledger.Labels))!;
        Assert.Equal(typeof(EfCoreJsonValueConverter<>), labels.GetValueConverter()!.GetType().GetGenericTypeDefinition());
        Assert.NotNull(labels.GetValueComparer());

        var other = db.Model.FindEntityType(typeof(Ledger))!.FindProperty(nameof(Ledger.Other))!;
        Assert.Equal(typeof(EfCoreJsonValueConverter<>), other.GetValueConverter()!.GetType().GetGenericTypeDefinition());
        Assert.NotNull(other.GetValueComparer());
    }

    [Fact]
    public async Task ConsumerConversion_OnDeclaredNestedColumn_IsPreserved() {
        Guid uid;
        {
            using var scope = _root!.CreateScope();
            var       db    = scope.ServiceProvider.GetRequiredService<ConversionDbContext>();

            var ledger = new Ledger {
                Uid    = Guid.NewGuid(),
                Groups = new() { ["a"] = ["x", "y"] },
            };
            db.Ledgers.Add(ledger);
            await db.SaveChangesAsync();
            uid = ledger.Uid;
        }

        {
            using var scope = _root!.CreateScope();
            var       db    = scope.ServiceProvider.GetRequiredService<ConversionDbContext>();

            var property = db.Model.FindEntityType(typeof(Ledger))!.FindProperty(nameof(Ledger.Groups))!;
            Assert.NotNull(property.GetValueConverter());
            Assert.IsNotType<EfCoreJsonValueConverter<Dictionary<string, List<string>>>>(property.GetValueConverter());

            var found = await db.Ledgers.FindAsync(uid);
            Assert.NotNull(found);
            Assert.NotNull(found.Groups);
            Assert.Equal(["x", "y"], found.Groups!["a"]);

            var raw = await db.Database.SqlQuery<string>($"SELECT Groups AS \"Value\" FROM Ledgers WHERE Uid = {uid}").SingleAsync();
            Assert.Equal("{\"a\":[\"x\",\"y\"]}", raw);
        }
    }

    #region Nested type: Ledger

    public sealed class Ledger
    {
        public Dictionary<string, string?>?  Tags  { get; set; }
        public List<string>?                Labels { get; set; }

        [Column]
        public Dictionary<string, List<string>>? Groups { get; set; }

        [Column]
        public Dictionary<string, List<int>>? Other { get; set; }

        public Guid Uid { get; set; }
    }

    #endregion

    #region Nested type: ConversionDbContext

    public sealed class ConversionDbContext : DbContext
    {
        public ConversionDbContext(DbContextOptions<ConversionDbContext> options) : base(options) { }

        public DbSet<Ledger> Ledgers { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder) {
            modelBuilder.Entity<Ledger>().HasKey(l => l.Uid);
            modelBuilder.Entity<Ledger>().Property(l => l.Tags).HasConversion(ConsumerConverters.Tags, ConsumerConverters.TagsComparer);
            modelBuilder.Entity<Ledger>().Property(l => l.Groups).HasConversion(ConsumerConverters.Groups, ConsumerConverters.GroupsComparer);
        }
    }

    #endregion

    private static class ConsumerConverters
    {
        public static readonly ValueConverter<Dictionary<string, string?>?, string> Tags = new(
            v => JoinTags(v),
            v => SplitTags(v));

        public static readonly ValueComparer<Dictionary<string, string?>?> TagsComparer = new(
            (a, b) => ReferenceEquals(a, b)
                   || (a != null && b != null && a.Count == b.Count && !a.Except(b).Any()),
            v => v == null ? 0 : v.Count,
            v => v == null ? null : new Dictionary<string, string?>(v));

        public static readonly ValueConverter<Dictionary<string, List<string>>?, string> Groups = new(
            v => JsonSerializer.Serialize(v ?? new Dictionary<string, List<string>>(), (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<Dictionary<string, List<string>>>(v) ?? new Dictionary<string, List<string>>());

        public static readonly ValueComparer<Dictionary<string, List<string>>?> GroupsComparer = new(
            (a, b) => ReferenceEquals(a, b)
                   || (a != null && b != null && a.Count == b.Count && !a.Except(b).Any()),
            v => v == null ? 0 : v.Count,
            v => v == null ? null : new Dictionary<string, List<string>>(v.ToDictionary(kv => kv.Key, kv => new List<string>(kv.Value))));

        private static string JoinTags(Dictionary<string, string?>? value) {
            return string.Join(';', value?.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}") ?? []);
        }

        private static Dictionary<string, string?>? SplitTags(string value) {
            return value.Length == 0
                ? new Dictionary<string, string?>()
                : value.Split(';', StringSplitOptions.RemoveEmptyEntries)
                       .Select(part => part.Split('=', 2))
                       .ToDictionary(p => p[0], p => (string?)p[1]);
        }
    }
}
