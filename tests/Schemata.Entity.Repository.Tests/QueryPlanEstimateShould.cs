using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Moq.Protected;
using Schemata.Entity.Repository.Estimation;
using Schemata.Entity.Repository.Tests.Fixtures;
using Xunit;

namespace Schemata.Entity.Repository.Tests;

[Trait("Category", "Unit")]
public class QueryPlanEstimateShould
{
    [Theory]
    [InlineData(QueryEstimateProvider.PostgreSql, "[{\"Plan\":{\"Plan Rows\":7,\"Plans\":[{\"Plan Rows\":900}]}}]", 7)]
    [InlineData(QueryEstimateProvider.MySql, "{\"query_block\":{\"table\":{\"rows_examined_per_scan\":900,\"rows_produced_per_join\":7}}}", 7)]
    [InlineData(QueryEstimateProvider.MySql, "{\"query_block\":{\"nested_loop\":[{\"table\":{\"rows_produced_per_join\":900}},{\"table\":{\"rows_produced_per_join\":7}}]}}", 7)]
    [InlineData(QueryEstimateProvider.MySql, "{\"query_block\":{\"ordering_operation\":{\"table\":{\"rows_produced_per_join\":7}}}}", 7)]
    public void Parse_ResultCardinality_IgnoresChildScans(QueryEstimateProvider provider, string plan, long expected) {
        Assert.Equal(expected, QueryPlanEstimate.Parse(plan, provider));
    }

    [Theory]
    [InlineData("{\"query_block\":{\"table\":{\"rows_examined_per_scan\":900}}}")]
    [InlineData("{\"query_block\":{\"grouping_operation\":{\"table\":{\"rows_produced_per_join\":900}}}}")]
    [InlineData("{\"query_block\":{\"nested_loop\":[{\"table\":{\"rows_produced_per_join\":900}},{\"table\":{\"rows_produced_per_join\":7,\"first_match\":\"a\"}}]}}")]
    public void Parse_UnsupportedMySqlOutput_ReturnsNull(string plan) {
        Assert.Null(QueryPlanEstimate.Parse(plan, QueryEstimateProvider.MySql));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("0.25", 1)]
    [InlineData("9.223372036854775807e18", long.MaxValue)]
    public void Parse_NonnegativeRows_RoundsUpWithinInt64(string value, long expected) {
        Assert.Equal(expected, QueryPlanEstimate.Parse("[{\"Plan\":{\"Plan Rows\":" + value + "}}]", QueryEstimateProvider.PostgreSql));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("9223372036854775808")]
    [InlineData("1e100")]
    [InlineData("\"not-a-number\"")]
    public void Parse_InvalidCardinality_Throws(string value) {
        Assert.Throws<FormatException>(() => QueryPlanEstimate.Parse("[{\"Plan\":{\"Plan Rows\":" + value + "}}]", QueryEstimateProvider.PostgreSql));
    }

    [Fact]
    public void Parse_InvalidJson_PropagatesParsingFailure() {
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => QueryPlanEstimate.Parse("not-json", QueryEstimateProvider.MySql));
    }

    [Fact]
    public void Parse_SqlServer_UsesResultOperator() {
        Assert.Equal(4, QueryPlanEstimate.Parse(SqlServerPlan, QueryEstimateProvider.SqlServer));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Estimate_CancelledDuringShowplan_RestoresSession(bool duringEnable) {
        using var cancellation = new CancellationTokenSource();
        var command = CreateModeCommand(out var connection);
        var enabled = false;
        command.Setup(item => item.ExecuteNonQueryAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken token) => {
                if (command.Object.CommandText == "SET SHOWPLAN_XML ON") {
                    enabled = true;
                    if (duringEnable) {
                        cancellation.Cancel();
                        token.ThrowIfCancellationRequested();
                    }
                } else {
                    Assert.False(token.CanBeCanceled);
                    Assert.InRange(command.Object.CommandTimeout, 1, 30);
                    enabled = false;
                }
                return Task.FromResult(0);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => QueryPlanEstimate.EstimateSqlServerAsync(
            connection.Object, null, 0, token => {
                cancellation.Cancel();
                return Task.FromCanceled<string>(token);
            }, cancellation.Token));

        Assert.False(enabled);
        connection.Verify(item => item.CloseAsync(), Times.Never);
    }

    [Fact]
    public async Task Estimate_RestorationFails_ClosesConnectionAndPreservesBothFailures() {
        var command = CreateModeCommand(out var connection);
        var original = new InvalidOperationException("plan failed");
        var restoration = new InvalidOperationException("restore failed");
        command.Setup(item => item.ExecuteNonQueryAsync(It.IsAny<CancellationToken>()))
            .Returns(() => command.Object.CommandText == "SET SHOWPLAN_XML ON"
                ? Task.FromResult(0) : Task.FromException<int>(restoration));
        connection.Setup(item => item.CloseAsync()).Returns(Task.CompletedTask);

        var error = await Assert.ThrowsAsync<AggregateException>(() => QueryPlanEstimate.EstimateSqlServerAsync(
            connection.Object, null, 12, _ => Task.FromException<string>(original)));

        Assert.Collection(error.InnerExceptions, item => Assert.Same(original, item), item => Assert.Same(restoration, item));
        connection.Verify(item => item.CloseAsync(), Times.Once);
    }

    [Fact]
    public async Task Estimate_SqlServerSuccess_RestoresModeAndRetainsTransaction() {
        var command = CreateModeCommand(out var connection);
        var transaction = new Mock<DbTransaction>().Object;
        var enabled = false;
        command.Setup(item => item.ExecuteNonQueryAsync(It.IsAny<CancellationToken>()))
            .Returns(() => {
                Assert.Same(transaction, command.Object.Transaction);
                enabled = command.Object.CommandText == "SET SHOWPLAN_XML ON";
                return Task.FromResult(0);
            });

        var rows = await QueryPlanEstimate.EstimateSqlServerAsync(connection.Object, transaction, 12, _ => {
            Assert.True(enabled);
            return Task.FromResult(SqlServerPlan);
        });

        Assert.Equal(4, rows);
        Assert.False(enabled);
    }

    [Fact]
    public async Task Estimate_PlanFailure_RestoresCallerCommandText() {
        var command = new Mock<DbCommand>();
        command.SetupProperty(item => item.CommandText, "SELECT value FROM records WHERE value = @value");
        var original = new InvalidOperationException("database unavailable");
        command.Setup(item => item.ExecuteScalarAsync(It.IsAny<CancellationToken>())).ThrowsAsync(original);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => QueryPlanEstimate.EstimateAsync(command.Object, QueryEstimateProvider.PostgreSql));

        Assert.Same(original, error);
        Assert.Equal("SELECT value FROM records WHERE value = @value", command.Object.CommandText);
    }

    [Fact]
    public async Task Estimate_AlreadyCancelled_DoesNotExecuteCommand() {
        var command = new Mock<DbCommand>(MockBehavior.Strict);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => QueryPlanEstimate.EstimateAsync(
            command.Object, QueryEstimateProvider.PostgreSql, new CancellationToken(true)));
        command.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Estimate_BaseUnsupported_DoesNotCount() {
        using var services = new ServiceCollection().BuildServiceProvider();
        var repository = new Mock<RepositoryBase<PlainEntity>>(services) { CallBase = true };
        repository.Setup(item => item.LongCountAsync<PlainEntity>(null, It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("Exact count must not execute."));

        Assert.Null(await repository.Object.EstimateCountAsync<PlainEntity>(null));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.Object.EstimateCountAsync<PlainEntity>(null, new CancellationToken(true)).AsTask());
        repository.Verify(item => item.LongCountAsync<PlainEntity>(null, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Estimate_InterfaceDefaultUnsupported_DoesNotCount() {
        var repository = new Mock<IRepository<PlainEntity>> { CallBase = true };
        repository.Setup(item => item.LongCountAsync<PlainEntity>(null, It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("Exact count must not execute."));

        Assert.Null(await repository.Object.EstimateCountAsync<PlainEntity>(null));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.Object.EstimateCountAsync<PlainEntity>(null, new CancellationToken(true)).AsTask());
        repository.Verify(item => item.LongCountAsync<PlainEntity>(null, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Estimate_InvalidProvider_ThrowsWithoutExecution() {
        var command = new Mock<DbCommand>();
        command.SetupProperty(item => item.CommandText, "SELECT value FROM records");

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => QueryPlanEstimate.EstimateAsync(command.Object, (QueryEstimateProvider)999));

        command.Verify(item => item.ExecuteScalarAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Mock<DbCommand> CreateModeCommand(out Mock<DbConnection> connection) {
        var command = new Mock<DbCommand>();
        command.SetupAllProperties();
        connection = new Mock<DbConnection>();
        connection.Protected().Setup<DbCommand>("CreateDbCommand").Returns(command.Object);
        return command;
    }

    private const string SqlServerPlan = """
        <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan">
          <BatchSequence><Batch><Statements><StmtSimple><QueryPlan>
            <RelOp EstimateRows="4"><Filter><RelOp EstimateRows="900" /></Filter></RelOp>
          </QueryPlan></StmtSimple></Statements></Batch></BatchSequence>
        </ShowPlanXML>
        """;
}
