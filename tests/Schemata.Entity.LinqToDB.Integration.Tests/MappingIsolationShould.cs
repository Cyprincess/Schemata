using System;
using System.Linq;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Entity.Repository;
using Xunit;

namespace Schemata.Entity.LinqToDB.Integration.Tests;

[Trait("Layer", "Integration")]
public class MappingIsolationShould
{
    [Fact]
    public async Task Independent_Options_Preserve_Application_Mappings_And_Default_Schema() {
        var baseline = MappingSchema.Default.GetEntityDescriptor(typeof(Row)).TableName;
        ServiceProvider Build(string table) {
            var mapping = new MappingSchema();
            new FluentMappingBuilder(mapping).Entity<Row>().HasTableName(table).Build();
            var services = new ServiceCollection();
            var builder = services.AddRepository<Row, LinqToDbRepository<Context, Row>>();
            builder.UseLinqToDb<Context>((_, options) => options.UseSQLite("Data Source=:memory:").UseMappingSchema(mapping));
            builder.UseLinqToDb<Context>((_, options) => options.UseSQLite("Data Source=:memory:"));
            return services.BuildServiceProvider();
        }
        using var first = Build("first_rows");
        using var second = Build("second_rows");
        await using var a = first.GetRequiredService<Func<Context>>()();
        await using var b = second.GetRequiredService<Func<Context>>()();
        await a.CreateTableAsync<Row>();
        await b.CreateTableAsync<Row>();
        await a.InsertAsync(new Row { Id = 1, Value = "first" });
        await b.InsertAsync(new Row { Id = 1, Value = "second" });
        Assert.Equal("first", a.GetTable<Row>().Single().Value);
        Assert.Equal("second", b.GetTable<Row>().Single().Value);
        Assert.Equal("first_rows", a.MappingSchema.GetEntityDescriptor(typeof(Row)).TableName);
        Assert.Equal("second_rows", b.MappingSchema.GetEntityDescriptor(typeof(Row)).TableName);
        Assert.Equal(baseline, MappingSchema.Default.GetEntityDescriptor(typeof(Row)).TableName);
    }

    public sealed class Context(DataOptions<Context> options) : DataConnection(options.Options);
    [System.ComponentModel.DataAnnotations.Schema.Table("attribute_rows")]
    public sealed class Row
    {
        [PrimaryKey]
        public int Id { get; set; }
        public string Value { get; set; } = null!;
    }
}
