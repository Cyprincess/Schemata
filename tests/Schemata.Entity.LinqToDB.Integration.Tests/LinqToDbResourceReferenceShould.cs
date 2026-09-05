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
using Xunit;
using TableAttribute = System.ComponentModel.DataAnnotations.Schema.TableAttribute;

namespace Schemata.Entity.LinqToDB.Integration.Tests;

[Trait("Category", "Integration")]
public class LinqToDbResourceReferenceShould : IAsyncLifetime
{
    private readonly string _dbPath = $"{Guid.NewGuid():n}.db";

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
    public void AssociationJoin_OnResourceReferencedField_ResolvesTheReferencedRow() {
        using var connection = new DataConnection(_options);
        connection.Insert(new Book {
            Uid           = Guid.NewGuid(),
            Name          = "les-miserables",
            CanonicalName = "books/les-miserables",
        });
        connection.Insert(new Review {
            Uid               = Guid.NewGuid(),
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
    public void JsonConverter_Creates_Json_Columns_And_Roundtrips() {
        using var diagnostic = new DataConnection(_options);
        var columns = diagnostic.Query<string>(
                                     "SELECT name FROM pragma_table_info('rr_books')")
                                .ToList();
        Assert.Contains("Counters", columns);
        Assert.Contains("Tags", columns);

        var uid = Guid.NewGuid();
        {
            using var connection = new DataConnection(_options);
            connection.Insert(new Book {
                Uid           = uid,
                Name          = "json-column-test",
                CanonicalName = "books/json-column-test",
                Counters      = new() { ["views"] = 3 },
                Tags          = ["classic"],
            });
        }

        {
            using var connection = new DataConnection(_options);
            var found = connection.GetTable<Book>().Single(b => b.Uid == uid);
            Assert.Equal(3, found.Counters!["views"]);
            Assert.Equal(["classic"], found.Tags);
        }
    }

    #region Nested type: Book

    [Table("rr_books")]
    [Abstractions.Entities.PrimaryKey(nameof(Uid))]
    public sealed class Book : IIdentifier, ICanonicalName
    {
        public Dictionary<string, int>? Counters { get; set; }
        public List<string>?            Tags     { get; set; }

        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }

        public Guid Uid { get; set; }
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
