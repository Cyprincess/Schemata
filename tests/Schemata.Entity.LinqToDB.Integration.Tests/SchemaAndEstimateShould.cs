using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LinqToDB.Data;
using Microsoft.Extensions.DependencyInjection;
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
                await repository.AddAsync(new() { Uid = Guid.NewGuid(), FullName = $"Student {i}" });
            }

            await repository.CommitAsync();
        }

        using var statsScope = _fixture.ServiceProvider.CreateScope();
        var connection = statsScope.ServiceProvider.GetRequiredService<TestDataConnection>();
        connection.Execute("ANALYZE");
        connection.Execute("UPDATE sqlite_stat1 SET stat = '123 1' WHERE tbl = 'Students'");
        connection.Execute("CREATE INDEX a_partial_students ON Students(Grade) WHERE Grade = 99");
        connection.Execute("INSERT INTO sqlite_stat1(tbl, idx, stat) VALUES ('Students', 'a_partial_students', '1 1')");

        var (estimatedRepository, estimatedScope) = _fixture.CreateScopeWithRepository();
        using (estimatedScope) {
            Assert.Null(await estimatedRepository.EstimateCountAsync<Student>(null));
            using var suppression = estimatedRepository.SuppressQuerySoftDelete();
            Assert.Equal(123L, await estimatedRepository.EstimateCountAsync<Student>(null));
            Assert.Null(await estimatedRepository.EstimateCountAsync(q => q.Where(student => student.Grade == 2)));
            Assert.Null(await estimatedRepository.EstimateCountAsync(q => q.Take(1)));
            Assert.Null(await estimatedRepository.EstimateCountAsync(q => q.Select(student => student.Grade)));
        }
    }

    [Fact]
    public async Task EstimateCountAsync_MissingStatistics_ReturnsNull() {
        var (repository, scope) = _fixture.CreateScopeWithRepository();
        using (scope) {
            using var suppression = repository.SuppressQuerySoftDelete();
            Assert.Null(await repository.EstimateCountAsync<Student>(null));
        }
    }

    [Fact]
    public async Task EstimateCountAsync_Cancelled_PropagatesCancellation() {
        var (repository, scope) = _fixture.CreateScopeWithRepository();
        using (scope) {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.EstimateCountAsync<Student>(null, new CancellationToken(true)).AsTask());
        }
    }
}
