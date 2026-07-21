using System.Linq;
using Xunit;

namespace Schemata.Entity.LinqToDB.Integration.Tests;

public class EstimateQueriesShould
{
    [Theory]
    [InlineData("PostgreSQL.17", "PostgreSql")]
    [InlineData("MySql.8", "MySql")]
    [InlineData("MariaDB.11", "MySql")]
    [InlineData("SqlServer.2022", "SqlServer")]
    [InlineData("SQLite.MS", "Sqlite")]
    [InlineData("Oracle", "None")]
    public void GetProvider_FamilyName_ReturnsExpectedProvider(string name, string expected) {
        Assert.Equal(expected, EstimateQueries.GetProvider(name).ToString());
    }

    [Fact]
    public void TryParsePostgreSql_RecordedPlan_ReturnsPlanRows() {
        const string json = "[{\"Plan\":{\"Node Type\":\"Seq Scan\",\"Plan Rows\":42}}]";

        var parsed = EstimateQueries.TryParsePostgreSql(json, out var rows);

        Assert.True(parsed);
        Assert.Equal(42L, rows);
    }

    [Theory]
    [InlineData("{\"query_block\":{\"rows_examined_per_scan\":31}}", 31)]
    [InlineData("{\"query_block\":{\"nested_loop\":[{\"rows_produced_per_join\":17}]}}", 17)]
    public void TryParseMySql_RecordedPlan_ReturnsEstimatedRows(string json, long expected) {
        var parsed = EstimateQueries.TryParseMySql(json, out var rows);

        Assert.True(parsed);
        Assert.Equal(expected, rows);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not-json")]
    public void Parsers_MalformedOrMissingPlan_ReturnFalse(string json) {
        Assert.False(EstimateQueries.TryParsePostgreSql(json, out _));
        Assert.False(EstimateQueries.TryParseMySql(json, out _));
    }

    [Fact]
    public void HasWhere_WhereExpression_ReturnsTrue() {
        Assert.True(EstimateQueries.HasWhere(new[] { 1 }.AsQueryable().Where(value => value == 1).Expression));
    }
}
