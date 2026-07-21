using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Common;
using Xunit;

namespace Schemata.Entity.EntityFrameworkCore.Integration.Tests;

[Trait("Category", "Integration")]
public class SchemataModelCustomizerShould : IAsyncLifetime
{
    private readonly string           _dbPath = $"{Identifiers.NewUid():n}.db";
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
    public async Task JsonConverter_OnDictionaryStringString_Roundtrips() {
        Guid bookUid;
        {
            using var scope = _root!.CreateScope();
            var       db    = scope.ServiceProvider.GetRequiredService<CustomizerDbContext>();

            var book = new Book {
                Uid           = Identifiers.NewUid(),
                Name          = "dict-test",
                CanonicalName = "books/dict-test",
                Metadata = new() {
                    ["author"]  = "Hugo",
                    ["edition"] = "1862",
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
            Assert.NotNull(found!.Metadata);
            Assert.Equal("Hugo", found.Metadata!["author"]);
            Assert.Equal("1862", found.Metadata["edition"]);
        }
    }

    [Fact]
    public async Task JsonConverter_OnNullableValueDictionary_Roundtrips() {
        Guid bookUid;
        {
            using var scope = _root!.CreateScope();
            var       db    = scope.ServiceProvider.GetRequiredService<CustomizerDbContext>();

            var book = new Book {
                Uid           = Identifiers.NewUid(),
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
            Assert.NotNull(found!.Annotations);
            Assert.Equal("fr", found.Annotations!["language"]);
            Assert.Null(found.Annotations["origin"]);
        }
    }

    [Fact]
    public async Task JsonConverter_OnCollectionString_Roundtrips() {
        Guid bookUid;
        {
            using var scope = _root!.CreateScope();
            var       db    = scope.ServiceProvider.GetRequiredService<CustomizerDbContext>();

            var book = new Book {
                Uid           = Identifiers.NewUid(),
                Name          = "list-test",
                CanonicalName = "books/list-test",
                Tags          = ["classic", "french", "novel"],
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
            Assert.NotNull(found!.Tags);
            Assert.Equal(3, found.Tags!.Count);
            Assert.Contains("french", found.Tags);
        }
    }

    [Fact]
    public async Task JsonConverter_OnInterfaceCollectionString_Roundtrips() {
        Guid bookUid;
        {
            using var scope = _root!.CreateScope();
            var       db    = scope.ServiceProvider.GetRequiredService<CustomizerDbContext>();

            var book = new Book {
                Uid           = Identifiers.NewUid(),
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
            Assert.NotNull(found!.Aliases);
            Assert.Equal(["les-mis", "the-miserables"], found.Aliases!);
        }
    }

    [Fact]
    public async Task JsonConverter_OnDictionaryStringInt_Roundtrips() {
        Guid bookUid;
        {
            using var scope = _root!.CreateScope();
            var       db    = scope.ServiceProvider.GetRequiredService<CustomizerDbContext>();

            var book = new Book {
                Uid           = Identifiers.NewUid(),
                Name          = "int-dict-test",
                CanonicalName = "books/int-dict-test",
                Counters = new() {
                    ["views"] = 3,
                    ["likes"] = 5,
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
            Assert.NotNull(found!.Counters);
            Assert.Equal(3, found.Counters!["views"]);
            Assert.Equal(5, found.Counters["likes"]);
        }
    }

    [Fact]
    public async Task JsonConverter_OnCollectionInt_Roundtrips() {
        Guid bookUid;
        {
            using var scope = _root!.CreateScope();
            var       db    = scope.ServiceProvider.GetRequiredService<CustomizerDbContext>();

            var book = new Book {
                Uid           = Identifiers.NewUid(),
                Name          = "int-list-test",
                CanonicalName = "books/int-list-test",
                Ratings       = [1, 2, 3],
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
            Assert.NotNull(found!.Ratings);
            Assert.Equal([1, 2, 3], found.Ratings!);
        }
    }

    [Fact]
    public async Task JsonConverter_OnDictionaryStringInt_InPlaceMutationPersists() {
        Guid bookUid;
        {
            using var scope = _root!.CreateScope();
            var       db    = scope.ServiceProvider.GetRequiredService<CustomizerDbContext>();

            var book = new Book {
                Uid           = Identifiers.NewUid(),
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
            Assert.NotNull(found!.Counters);

            found.Counters!["views"] = 2;
            await db.SaveChangesAsync();
        }

        {
            using var scope = _root!.CreateScope();
            var       db    = scope.ServiceProvider.GetRequiredService<CustomizerDbContext>();
            var       found = await db.Books.FindAsync(bookUid);
            Assert.NotNull(found);
            Assert.Equal(2, found!.Counters!["views"]);
        }
    }

    [Fact]
    public async Task JsonConverter_OnEnumCollectionAndDictionary_Roundtrips() {
        Guid bookUid;
        {
            using var scope = _root!.CreateScope();
            var       db    = scope.ServiceProvider.GetRequiredService<CustomizerDbContext>();

            var book = new Book {
                Uid           = Identifiers.NewUid(),
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
            Assert.Equal([Book.Shelf.Fiction, Book.Shelf.Science], found!.Genres!);
            Assert.Equal(Book.Shelf.History, found.ShelfByName!["primary"]);
        }
    }

    [Fact]
    public void JsonConverter_OnByteArray_UsesNativeBinaryMapping() {
        using var scope = _root!.CreateScope();
        var       db    = scope.ServiceProvider.GetRequiredService<CustomizerDbContext>();

        var property = db.Model.FindEntityType(typeof(Book))!.FindProperty(nameof(Book.Payload));
        Assert.NotNull(property);
        Assert.Null(property!.GetValueConverter());
    }

    #region Nested type: Book

    public sealed class Book : IIdentifier, ICanonicalName
    {
        public Dictionary<string, string>?  Metadata    { get; set; }
        public Dictionary<string, string?>? Annotations { get; set; }
        public List<string>?                Tags        { get; set; }
        public ICollection<string>?         Aliases     { get; set; }
        public Dictionary<string, int>?     Counters    { get; set; }
        public List<int>?                   Ratings     { get; set; }
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
