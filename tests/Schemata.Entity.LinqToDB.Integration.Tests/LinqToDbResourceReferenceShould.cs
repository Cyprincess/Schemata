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
using PrimaryKeyAttribute = Microsoft.EntityFrameworkCore.PrimaryKeyAttribute;
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

    #region Nested type: Book

    [Table("rr_books")]
    [PrimaryKey(nameof(Uid))]
    public sealed class Book : IIdentifier, ICanonicalName
    {
        public Dictionary<string, string>? Metadata { get; set; }

        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }

        public Guid Uid { get; set; }
    }

    #endregion

    #region Nested type: Review

    [Table("rr_reviews")]
    [PrimaryKey(nameof(Uid))]
    public sealed class Review : IIdentifier
    {
        [ResourceReference(typeof(Book))]
        public string? BookCanonicalName { get; set; }

        public int Rating { get; set; }

        public Guid Uid { get; set; }
    }

    #endregion
}
