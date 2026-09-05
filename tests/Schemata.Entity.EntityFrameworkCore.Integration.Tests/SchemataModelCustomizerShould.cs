using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Xunit;

namespace Schemata.Entity.EntityFrameworkCore.Integration.Tests;

[Trait("Category", "Integration")]
public class SchemataModelCustomizerShould : IAsyncLifetime
{
    private readonly string           _dbPath = $"{Guid.NewGuid():n}.db";
    private          ServiceProvider? _root;

    #region IAsyncLifetime Members

    public async Task InitializeAsync() {
        var services = new ServiceCollection();
        services.AddDbContextFactory<CustomizerDbContext>(options => {
            options.UseSqlite($"Data Source={_dbPath}");
            options.ReplaceService<IModelCustomizer, SchemataModelCustomizer>();
        });
        _root = services.BuildServiceProvider();

        using var scope = _root.CreateScope();
        var       db    = scope.ServiceProvider.GetRequiredService<CustomizerDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() {
        if (_root is not null) {
            using (var scope = _root.CreateScope()) {
                var db = scope.ServiceProvider.GetRequiredService<CustomizerDbContext>();
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
    public async Task JsonConverter_OnNullableValueDictionary_Roundtrips() {
        Guid bookUid;
        {
            using var scope = _root!.CreateScope();
            var       db    = scope.ServiceProvider.GetRequiredService<CustomizerDbContext>();

            var book = new Book {
                Uid           = Guid.NewGuid(),
                Name          = "nullable-dict-test",
                CanonicalName = "books/nullable-dict-test",
                Annotations = new() {
                    ["language"] = "fr",
                    ["origin"]   = null,
                },
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookUid = book.Uid;
        }

        {
            using var scope = _root!.CreateScope();
            var       db    = scope.ServiceProvider.GetRequiredService<CustomizerDbContext>();
            var       found = await db.Books.FindAsync(bookUid);
            Assert.NotNull(found);
            Assert.NotNull(found.Annotations);
            Assert.Equal("fr", found.Annotations!["language"]);
            Assert.Null(found.Annotations["origin"]);
        }
    }

    [Fact]
    public async Task JsonConverter_OnInterfaceCollectionString_Roundtrips() {
        Guid bookUid;
        {
            using var scope = _root!.CreateScope();
            var       db    = scope.ServiceProvider.GetRequiredService<CustomizerDbContext>();

            var book = new Book {
                Uid           = Guid.NewGuid(),
                Name          = "interface-list-test",
                CanonicalName = "books/interface-list-test",
                Aliases       = ["les-mis", "the-miserables"],
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookUid = book.Uid;
        }

        {
            using var scope = _root!.CreateScope();
            var       db    = scope.ServiceProvider.GetRequiredService<CustomizerDbContext>();
            var       found = await db.Books.FindAsync(bookUid);
            Assert.NotNull(found);
            Assert.NotNull(found.Aliases);
            Assert.Equal(["les-mis", "the-miserables"], found.Aliases!);
        }
    }

    [Fact]
    public async Task JsonConverter_OnDictionaryStringInt_InPlaceMutationPersists() {
        Guid bookUid;
        {
            using var scope = _root!.CreateScope();
            var       db    = scope.ServiceProvider.GetRequiredService<CustomizerDbContext>();

            var book = new Book {
                Uid           = Guid.NewGuid(),
                Name          = "int-dict-mutation-test",
                CanonicalName = "books/int-dict-mutation-test",
                Counters = new() {
                    ["views"] = 1,
                },
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookUid = book.Uid;
        }

        {
            using var scope = _root!.CreateScope();
            var       db    = scope.ServiceProvider.GetRequiredService<CustomizerDbContext>();
            var       found = await db.Books.FindAsync(bookUid);
            Assert.NotNull(found);
            Assert.NotNull(found.Counters);

            found.Counters!["views"] = 2;
            await db.SaveChangesAsync();
        }

        {
            using var scope = _root!.CreateScope();
            var       db    = scope.ServiceProvider.GetRequiredService<CustomizerDbContext>();
            var       found = await db.Books.FindAsync(bookUid);
            Assert.NotNull(found);
            Assert.Equal(2, found.Counters!["views"]);
        }
    }

    [Fact]
    public async Task JsonConverter_OnEnumCollectionAndDictionary_Roundtrips() {
        Guid bookUid;
        {
            using var scope = _root!.CreateScope();
            var       db    = scope.ServiceProvider.GetRequiredService<CustomizerDbContext>();

            var book = new Book {
                Uid           = Guid.NewGuid(),
                Name          = "enum-test",
                CanonicalName = "books/enum-test",
                Genres        = [Book.Shelf.Fiction, Book.Shelf.Science],
                ShelfByName   = new() { ["primary"] = Book.Shelf.History },
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookUid = book.Uid;
        }

        {
            using var scope = _root!.CreateScope();
            var       db    = scope.ServiceProvider.GetRequiredService<CustomizerDbContext>();
            var       found = await db.Books.FindAsync(bookUid);
            Assert.NotNull(found);
            Assert.Equal([Book.Shelf.Fiction, Book.Shelf.Science], found.Genres!);
            Assert.Equal(Book.Shelf.History, found.ShelfByName!["primary"]);
        }
    }

    [Fact]
    public void JsonConverter_OnByteArray_UsesNativeBinaryMapping() {
        using var scope = _root!.CreateScope();
        var       db    = scope.ServiceProvider.GetRequiredService<CustomizerDbContext>();

        var property = db.Model.FindEntityType(typeof(Book))!.FindProperty(nameof(Book.Payload));
        Assert.NotNull(property);
        Assert.Null(property.GetValueConverter());
    }

    #region Nested type: Book

    public sealed class Book : IIdentifier, ICanonicalName
    {
        public Dictionary<string, string?>? Annotations { get; set; }
        public ICollection<string>?         Aliases     { get; set; }
        public Dictionary<string, int>?     Counters    { get; set; }
        public List<Shelf>?                 Genres      { get; set; }
        public Dictionary<string, Shelf>?   ShelfByName { get; set; }
        public byte[]?                      Payload     { get; set; }

        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }

        public Guid Uid { get; set; }

        public enum Shelf
        {
            Fiction,
            History,
            Science,
        }
    }

    #endregion

    #region Nested type: CustomizerDbContext

    public sealed class CustomizerDbContext : DbContext
    {
        public CustomizerDbContext(DbContextOptions<CustomizerDbContext> options) : base(options) { }

        public DbSet<Book> Books { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder) {
            modelBuilder.Entity<Book>().HasKey(b => b.Uid);
        }
    }

    #endregion
}
