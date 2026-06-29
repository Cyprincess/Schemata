using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
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
    public async Task AutoFkToCanonicalName_OnResourceReferencedField_Roundtrips() {
        Guid reviewUid;
        {
            using var scope = _root!.CreateScope();
            var       db    = scope.ServiceProvider.GetRequiredService<CustomizerDbContext>();

            var book = new Book {
                Uid           = Identifiers.NewUid(),
                Name          = "les-miserables",
                CanonicalName = "books/les-miserables",
            };
            db.Books.Add(book);

            var review = new Review {
                Uid          = Identifiers.NewUid(),
                BookCanonicalName = "books/les-miserables",
                Rating       = 5,
            };
            db.Reviews.Add(review);

            await db.SaveChangesAsync();
            reviewUid = review.Uid;
        }

        {
            using var scope = _root!.CreateScope();
            var       db    = scope.ServiceProvider.GetRequiredService<CustomizerDbContext>();
            var       found = await db.Reviews.FindAsync(reviewUid);
            Assert.NotNull(found);
            Assert.Equal("books/les-miserables", found!.BookCanonicalName);
        }
    }

    [Fact]
    public void AlternateKey_OnReferencedEntityCanonicalName_RejectsDuplicate() {
        using var scope = _root!.CreateScope();
        var       db    = scope.ServiceProvider.GetRequiredService<CustomizerDbContext>();

        db.Books.Add(new Book {
            Uid           = Identifiers.NewUid(),
            Name          = "duplicate",
            CanonicalName = "books/duplicate",
        });

        // Adding a second Book with the same CanonicalName should fail the alternate-key
        // uniqueness check that SchemataModelCustomizer registers; EF Core reports this
        // at change-tracker time rather than at SaveChanges time.
        var exception = Assert.Throws<InvalidOperationException>(() => db.Books.Add(new Book {
            Uid           = Identifiers.NewUid(),
            Name          = "another",
            CanonicalName = "books/duplicate",
        }));
        Assert.Contains("CanonicalName", exception.Message);
    }

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

    #region Nested type: Book

    public sealed class Book : IIdentifier, ICanonicalName
    {
        public Dictionary<string, string>?  Metadata    { get; set; }
        public Dictionary<string, string?>? Annotations { get; set; }
        public List<string>?                Tags        { get; set; }

        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }

        public Guid Uid { get; set; }
    }

    #endregion

    #region Nested type: Review

    public sealed class Review : IIdentifier
    {
        [ResourceReference(typeof(Book))]
        public string? BookCanonicalName { get; set; }

        public int Rating { get; set; }

        public Guid Uid { get; set; }
    }

    #endregion

    #region Nested type: CustomizerDbContext

    public sealed class CustomizerDbContext : DbContext
    {
        public CustomizerDbContext(DbContextOptions<CustomizerDbContext> options) : base(options) { }

        public DbSet<Book>   Books   { get; set; } = null!;
        public DbSet<Review> Reviews { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder) {
            modelBuilder.Entity<Book>().HasKey(b => b.Uid);
            modelBuilder.Entity<Review>().HasKey(r => r.Uid);
        }
    }

    #endregion
}
