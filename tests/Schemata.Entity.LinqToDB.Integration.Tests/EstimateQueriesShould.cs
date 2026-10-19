using System.Linq;
using LinqToDB;
using LinqToDB.Data;
using Schemata.Entity.LinqToDB.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Entity.LinqToDB.Integration.Tests;

[Trait("Category", "Unit")]
public class EstimateQueriesShould
{
    [Theory]
    [InlineData("PostgreSQL.17", "PostgreSql")]
    [InlineData("MySql.8", "MySql")]
    [InlineData("MariaDB.11", "None")]
    [InlineData("SqlServer.2022", "SqlServer")]
    [InlineData("SQLite.MS", "Sqlite")]
    [InlineData("Oracle", "None")]
    public void GetProvider_FamilyName_ReturnsExpectedProvider(string name, string expected) {
        Assert.Equal(expected, EstimateQueries.GetProvider(name).ToString());
    }

    [Fact]
    public void IsTableRoot_ChangedCardinalityOrProjection_RejectsQuery() {
        using var connection = new DataConnection(new DataOptions().UseSQLite("Data Source=:memory:"));
        var table = connection.GetTable<Student>().TableName("Students");

        Assert.True(EstimateQueries.IsTableRoot<Student>(table.OfType<Student>().Expression, "Students"));
        Assert.False(EstimateQueries.IsTableRoot<Student>(table.Where(item => item.Grade == 2).Expression, "Students"));
        Assert.False(EstimateQueries.IsTableRoot<Student>(table.Take(1).Expression, "Students"));
        Assert.False(EstimateQueries.IsTableRoot<Student>(table.Select(item => item.Grade).Expression, "Students"));
        Assert.False(EstimateQueries.IsTableRoot<Student>(table.Concat(table).Expression, "Students"));
        Assert.False(EstimateQueries.IsTableRoot<Student>(table.TableName("OtherStudents").Expression, "Students"));
    }

    [Fact]
    public void HasMySqlUnsupportedShape_LimitOrGrouping_RejectsQuery() {
        var query = new[] { 1, 2 }.AsQueryable();

        Assert.True(EstimateQueries.HasMySqlUnsupportedShape(query.Take(1).Expression));
        Assert.True(EstimateQueries.HasMySqlUnsupportedShape(query.Skip(1).Expression));
        Assert.True(EstimateQueries.HasMySqlUnsupportedShape(query.GroupBy(value => value).Expression));
        Assert.False(EstimateQueries.HasMySqlUnsupportedShape(query.Where(value => value > 1).Expression));
    }
}
