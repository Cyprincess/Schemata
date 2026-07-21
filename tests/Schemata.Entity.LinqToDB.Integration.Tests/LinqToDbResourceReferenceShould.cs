using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using Microsoft.Data.Sqlite;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Xunit;
using TableAttribute = System.ComponentModel.DataAnnotations.Schema.TableAttribute;

namespace Schemata.Entity.LinqToDB.Integration.Tests;

[Trait("Category", "Integration")]
public class LinqToDbResourceReferenceShould : IAsyncLifetime
{
    private readonly string _dbPath = $"{Identifiers.NewUid():n}.db";

    private DataOptions _options = null!;

    #region IAsyncLifetime Members

    public Task InitializeAsync() {
        var schema = new MappingSchema();
        schema.AddMetadataReader(new SystemComponentModelDataAnnotationsSchemaAttributeReader());

        _options = new DataOptions()
                  .UseSQLite($"Data Source={_dbPath}")
                  .UseMappingSchema(schema);

        using var connection = new DataConnection(_options);
        connection.CreateTable<Book>(tableOptions:   TableOptions.CreateIfNotExists);
        connection.CreateTable<Review>(tableOptions: TableOptions.CreateIfNotExists);

        return Task.CompletedTask;
    }

    public Task DisposeAsync() {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) {
            File.Delete(_dbPath);
        }

        return Task.CompletedTask;
    }

    #endregion

    [Fact]
    public void AssociationJoin_OnResourceReferencedField_Works() {
        using var connection = new DataConnection(_options);
        connection.Insert(new Book {
            Uid           = Identifiers.NewUid(),
            Name          = "les-miserables",
            CanonicalName = "books/les-miserables",
        });
        connection.Insert(new Review {
            Uid               = Identifiers.NewUid(),
            BookCanonicalName = "books/les-miserables",
            Rating            = 5,
        });

        var query = from r in connection.GetTable<Review>()
                    join b in connection.GetTable<Book>() on r.BookCanonicalName equals b.CanonicalName
                    select new { r.Rating, b.Name, };

        var result = query.ToList();
        var single = Assert.Single(result);
        Assert.Equal("les-miserables", single.Name);
        Assert.Equal(5, single.Rating);
    }

    [Fact]
    public void JsonConverter_OnNullableValueDictionary_Roundtrips() {
        using var diagnostic = new DataConnection(_options);
        var columns = diagnostic.Query<string>(
                                     "SELECT name FROM pragma_table_info('rr_books')")
                                .ToList();
        Assert.Contains("Annotations", columns);

        var uid = Identifiers.NewUid();
        {
            using var connection = new DataConnection(_options);
            connection.Insert(new Book {
                Uid           = uid,
                Name          = "nullable-dict-test",
                CanonicalName = "books/nullable-dict-test",
                Annotations = new() {
                    ["language"] = "fr",
                    ["origin"]   = null,
                },
            });
        }

        {
            using var connection = new DataConnection(_options);
            var found = connection.GetTable<Book>().Single(b => b.Uid == uid);
            Assert.NotNull(found.Annotations);
            Assert.Equal("fr", found.Annotations!["language"]);
            Assert.Null(found.Annotations["origin"]);
        }
    }

    [Fact]
    public void JsonConverter_OnDictionaryStringString_Roundtrips() {
        using var diagnostic = new DataConnection(_options);
        var columns = diagnostic.Query<string>(
                                     "SELECT name FROM pragma_table_info('rr_books')")
                                .ToList();
        Assert.Contains("Metadata", columns);

        var uid = Identifiers.NewUid();
        {
            using var connection = new DataConnection(_options);
            connection.Insert(new Book {
                Uid           = uid,
                Name          = "dict-test",
                CanonicalName = "books/dict-test",
                Metadata = new() {
                    ["author"]  = "Hugo",
                    ["edition"] = "1862",
                },
            });
        }

        {
            using var connection = new DataConnection(_options);
            var found = connection.GetTable<Book>().Single(b => b.Uid == uid);
            Assert.NotNull(found.Metadata);
            Assert.Equal("Hugo", found.Metadata!["author"]);
            Assert.Equal("1862", found.Metadata["edition"]);
        }
    }

    [Fact]
    public void JsonConverter_OnDictionaryStringInt_Roundtrips() {
        using var diagnostic = new DataConnection(_options);
        var columns = diagnostic.Query<string>(
                                     "SELECT name FROM pragma_table_info('rr_books')")
                                .ToList();
        Assert.Contains("Counters", columns);

        var uid = Identifiers.NewUid();
        {
            using var connection = new DataConnection(_options);
            connection.Insert(new Book {
                Uid           = uid,
                Name          = "int-dict-test",
                CanonicalName = "books/int-dict-test",
                Counters = new() {
                    ["views"] = 3,
                    ["likes"] = 5,
                },
            });
        }

        {
            using var connection = new DataConnection(_options);
            var found = connection.GetTable<Book>().Single(b => b.Uid == uid);
            Assert.NotNull(found.Counters);
            Assert.Equal(3, found.Counters!["views"]);
            Assert.Equal(5, found.Counters["likes"]);
        }
    }

    [Fact]
    public void JsonConverter_OnCollectionInt_Roundtrips() {
        using var diagnostic = new DataConnection(_options);
        var columns = diagnostic.Query<string>(
                                     "SELECT name FROM pragma_table_info('rr_books')")
                                .ToList();
        Assert.Contains("Ratings", columns);

        var uid = Identifiers.NewUid();
        {
            using var connection = new DataConnection(_options);
            connection.Insert(new Book {
                Uid           = uid,
                Name          = "int-list-test",
                CanonicalName = "books/int-list-test",
                Ratings       = [1, 2, 3],
            });
        }

        {
            using var connection = new DataConnection(_options);
            var found = connection.GetTable<Book>().Single(b => b.Uid == uid);
            Assert.NotNull(found.Ratings);
            Assert.Equal([1, 2, 3], found.Ratings!);
        }
    }

    [Fact]
    public void JsonConverter_OnCollectionString_Roundtrips() {
        using var diagnostic = new DataConnection(_options);
        var columns = diagnostic.Query<string>(
                                     "SELECT name FROM pragma_table_info('rr_books')")
                                .ToList();
        Assert.Contains("Tags", columns);

        var uid = Identifiers.NewUid();
        {
            using var connection = new DataConnection(_options);
            connection.Insert(new Book {
                Uid           = uid,
                Name          = "string-list-test",
                CanonicalName = "books/string-list-test",
                Tags          = ["classic", "french", "novel"],
            });
        }

        {
            using var connection = new DataConnection(_options);
            var found = connection.GetTable<Book>().Single(b => b.Uid == uid);
            Assert.NotNull(found.Tags);
            Assert.Equal(["classic", "french", "novel"], found.Tags!);
        }
    }

    [Fact]
    public void JsonConverter_OnInterfaceCollectionString_Roundtrips() {
        using var diagnostic = new DataConnection(_options);
        var columns = diagnostic.Query<string>(
                                     "SELECT name FROM pragma_table_info('rr_books')")
                                .ToList();
        Assert.Contains("Aliases", columns);

        var uid = Identifiers.NewUid();
        {
            using var connection = new DataConnection(_options);
            connection.Insert(new Book {
                Uid           = uid,
                Name          = "interface-list-test",
                CanonicalName = "books/interface-list-test",
                Aliases       = ["les-mis", "the-miserables"],
            });
        }

        {
            using var connection = new DataConnection(_options);
            var found = connection.GetTable<Book>().Single(b => b.Uid == uid);
            Assert.NotNull(found.Aliases);
            Assert.Equal(["les-mis", "the-miserables"], found.Aliases!);
        }
    }

    [Fact]
    public void JsonConverter_OnEnumCollectionAndDictionary_Roundtrips() {
        using var diagnostic = new DataConnection(_options);
        var columns = diagnostic.Query<string>(
                                     "SELECT name FROM pragma_table_info('rr_books')")
                                .ToList();
        Assert.Contains("Genres", columns);
        Assert.Contains("ShelfByName", columns);

        var uid = Identifiers.NewUid();
        {
            using var connection = new DataConnection(_options);
            connection.Insert(new Book {
                Uid           = uid,
                Name          = "enum-test",
                CanonicalName = "books/enum-test",
                Genres        = [Book.Shelf.Fiction, Book.Shelf.Science],
                ShelfByName   = new() { ["primary"] = Book.Shelf.History },
            });
        }

        {
            using var connection = new DataConnection(_options);
            var found = connection.GetTable<Book>().Single(b => b.Uid == uid);
            Assert.Equal([Book.Shelf.Fiction, Book.Shelf.Science], found.Genres!);
            Assert.Equal(Book.Shelf.History, found.ShelfByName!["primary"]);
        }
    }

    [Fact]
    public void JsonConverter_OnByteArray_UsesNativeBinaryMapping() {
        using var diagnostic = new DataConnection(_options);
        var columns = diagnostic.Query<string>(
                                     "SELECT name FROM pragma_table_info('rr_books')")
                                .ToList();
        Assert.Contains("Payload", columns);

        var uid = Identifiers.NewUid();
        var payload = new byte[] { 1, 2, 3, };
        {
            using var connection = new DataConnection(_options);
            connection.Insert(new Book {
                Uid           = uid,
                Name          = "binary-test",
                CanonicalName = "books/binary-test",
                Payload       = payload,
            });
        }

        {
            using var connection = new DataConnection(_options);
            var found = connection.GetTable<Book>().Single(b => b.Uid == uid);
            Assert.Equal(payload, found.Payload);
        }
    }

    #region Nested type: Book

    [Table("rr_books")]
    [Abstractions.Entities.PrimaryKey(nameof(Uid))]
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

    #region Nested type: Review

    [Table("rr_reviews")]
    [Abstractions.Entities.PrimaryKey(nameof(Uid))]
    public sealed class Review : IIdentifier
    {
        [ResourceReference(typeof(Book))]
        public string? BookCanonicalName { get; set; }

        public int Rating { get; set; }

        public Guid Uid { get; set; }
    }

    #endregion
}
