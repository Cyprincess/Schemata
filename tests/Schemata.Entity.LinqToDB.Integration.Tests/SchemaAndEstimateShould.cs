using System.Threading.Tasks;
using LinqToDB.Data;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Common;
using Schemata.Entity.LinqToDB.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Entity.LinqToDB.Integration.Tests;

[Trait("Category", "Integration")]
public class SchemaAndEstimateShould : IAsyncLifetime
{
    private readonly IntegrationFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public void CreateTableWithIndexes_UniqueSchemataIndex_AppearsInSqliteSchema() {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var connection = scope.ServiceProvider.GetRequiredService<TestDataConnection>();

        var count = connection.Execute<long>("SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'IX_Courses_Title'");

        Assert.Equal(1L, count);
    }

    [Theory]
    [InlineData("SQLite.MS", "INDEX IF NOT EXISTS")]
    [InlineData("PostgreSQL.17", "INDEX IF NOT EXISTS")]
    [InlineData("MySql.8", "INDEX IF NOT EXISTS")]
    [InlineData("SqlServer.2022", "IF NOT EXISTS (SELECT 1 FROM sys.indexes")]
    public void CreateIndexSql_ProviderFamily_UsesSupportedConditionalSyntax(string provider, string expected) {
        var sql = SchemaExtensions.CreateIndexSql(provider, "Courses", new([nameof(Course.Title)]) { IsUnique = true });

        Assert.Contains(expected, sql);
    }

    [Fact]
    public async Task EstimateCountAsync_AfterAnalyze_UsesSqliteStatistics() {
        var (repository, scope) = _fixture.CreateScopeWithRepository();
        using (scope) {
            for (var i = 0; i < 3; i++) {
                await repository.AddAsync(new() { Uid = Identifiers.NewUid(), FullName = $"Student {i}" });
            }

            await repository.CommitAsync();
        }

        using var statsScope = _fixture.ServiceProvider.CreateScope();
        var connection = statsScope.ServiceProvider.GetRequiredService<TestDataConnection>();
        connection.Execute("ANALYZE");
        var stat = connection.Execute<long>("SELECT CAST(MAX(stat) AS INTEGER) FROM sqlite_stat1 WHERE tbl = 'Students'");

        var (estimatedRepository, estimatedScope) = _fixture.CreateScopeWithRepository();
        using (estimatedScope) {
            Assert.Equal(stat, await estimatedRepository.EstimateCountAsync<Student>(null));
        }
    }
}
