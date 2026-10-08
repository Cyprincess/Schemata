using System;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Entity.EntityFrameworkCore.Integration.Tests.Fixtures;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Estimation;
using SQLitePCL;
using Xunit;

namespace Schemata.Entity.EntityFrameworkCore.Integration.Tests;

[Trait("Category", "Integration")]
public class CountEstimateShould
{
    [Fact]
    public async Task EstimateCommand_ExecutesReaderPolicy_WithParametersAndActiveTransaction() {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var policy = new EstimatePolicy();
        var options = new DbContextOptionsBuilder<EstimateContext>().UseSqlite(connection).AddInterceptors(policy).Options;
        await using var context = new EstimateContext(options);
        await context.Database.EnsureCreatedAsync();
        context.Students.AddRange(new Student { Uid = Guid.NewGuid(), FullName = "O'Brien", Age = 21 },
            new Student { Uid = Guid.NewGuid(), FullName = "Allowed", Age = 22 });
        await context.SaveChangesAsync();
        await using var transaction = await context.Database.BeginTransactionAsync();
        context.Database.SetCommandTimeout(19);
        var name = "O'Brien";
        await using var command = context.Students.TagWith("estimate {policy}")
            .Where(s => s.FullName == name).Select(s => s.FullName).CreateDbCommand();
        policy.Active = true;
        policy.Transaction = transaction.GetDbTransaction();

        var result = await EfCoreEstimateCommand.ReadAsync(context, command, command.CommandText, default);

        Assert.Equal("Allowed", result);
        Assert.Equal(1, policy.Readers);
        Assert.Equal(ConnectionState.Open, connection.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EstimateCommand_PolicyRejection_PreventsDatabaseExecution(bool nonQuery) {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var policy = new EstimatePolicy { Active = true, Reject = true };
        var options = new DbContextOptionsBuilder<EstimateContext>().UseSqlite(connection).AddInterceptors(policy).Options;
        await using var context = new EstimateContext(options);
        context.Database.SetCommandTimeout(19);
        var statements = 0;
        strdelegate_trace trace = (_, _) => statements++;
        raw.sqlite3_trace(connection.Handle, trace, null!);
        try {
            if (nonQuery) {
                var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    EfCoreEstimateCommand.ExecuteModeAsync(context, "CREATE TABLE forbidden(value TEXT)", 7, default));
                Assert.Same(policy.Failure, error);
                Assert.Equal(19, context.Database.GetCommandTimeout());
            } else {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT 'forbidden'";
                var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    EfCoreEstimateCommand.ReadAsync(context, command, command.CommandText, default));
                Assert.Same(policy.Failure, error);
            }
            Assert.Equal(0, statements);
        } finally {
            raw.sqlite3_trace(connection.Handle, (strdelegate_trace)null!, null!);
        }
    }

    private sealed class EstimatePolicy : DbCommandInterceptor {
        public bool Active { get; set; }
        public bool Reject { get; set; }
        public int Readers { get; private set; }
        public DbTransaction? Transaction { get; set; }
        public InvalidOperationException Failure { get; } = new("estimate policy rejected");

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default) {
            if (!Active) return ValueTask.FromResult(result);
            if (Reject) throw Failure;
            Readers++;
            Assert.Same(Transaction, command.Transaction);
            Assert.Equal(19, command.CommandTimeout);
            Assert.Equal(CommandSource.LinqQuery, eventData.CommandSource);
            Assert.Contains("{policy}", command.CommandText);
            var parameter = Assert.Single(command.Parameters.Cast<DbParameter>());
            Assert.Equal("O'Brien", parameter.Value);
            Assert.Equal(DbType.String, parameter.DbType);
            parameter.Value = "Allowed";
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken ct = default) {
            if (Active && Reject) throw Failure;
            return ValueTask.FromResult(result);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReturnNull_WithoutOptInOrForUnsupportedProvider_WithoutExecutingSql(bool optIn) {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = CreateServices(connection);
        if (optIn) {
            new SchemataRepositoryBuilder(services).WithCountEstimates<EstimateContext>(QueryEstimateProvider.PostgreSql);
        }
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<Student>>();
        var statements = 0;
        strdelegate_trace trace = (_, _) => statements++;
        raw.sqlite3_trace(connection.Handle, trace, null!);
        try {
            Assert.Null(await repository.EstimateCountAsync(q => q.Where(student => student.Grade == 2)));
            Assert.Equal(0, statements);
            Assert.Equal(ConnectionState.Open, connection.State);
        } finally {
            raw.sqlite3_trace(connection.Handle, (strdelegate_trace)null!, null!);
        }
    }

    [Fact]
    public async Task CustomEstimator_ReceivesVisibleParameterizedQueryAndActiveTransaction() {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var estimator = new Mock<IEfCoreCountEstimator<EstimateContext>>(MockBehavior.Strict);
        var services = CreateServices(connection);
        services.AddScoped(_ => estimator.Object);
        new SchemataRepositoryBuilder(services).WithCountEstimates<EstimateContext>(QueryEstimateProvider.PostgreSql);
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        await using var context = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<EstimateContext>>()
                                           .CreateDbContextAsync();
        await context.Database.EnsureCreatedAsync();
        var name = "O'Brien";
        context.Students.AddRange(
            new Student { Uid = Guid.NewGuid(), FullName = name, Grade = 2, Age = 21 },
            new Student { Uid = Guid.NewGuid(), FullName = name, Grade = 2, Age = 17 },
            new Student { Uid = Guid.NewGuid(), FullName = name, Grade = 2, Age = 22, DeleteTime = DateTime.UnixEpoch },
            new Student { Uid = Guid.NewGuid(), FullName = "Other", Grade = 2, Age = 23 });
        await context.SaveChangesAsync();
        using var cts = new CancellationTokenSource();
        estimator.Setup(e => e.EstimateAsync(It.IsAny<EstimateContext>(), It.IsAny<IQueryable<Student>>(), cts.Token))
                 .Returns((EstimateContext active, IQueryable<Student> query, CancellationToken ct) => InspectAsync(active, query, ct));
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<Student>>();

        Assert.Equal(1L, await repository.EstimateCountAsync(q => q.Where(student => student.FullName == name), cts.Token));
        Assert.Equal(ConnectionState.Open, connection.State);

        async ValueTask<long?> InspectAsync(EstimateContext active, IQueryable<Student> query, CancellationToken ct) {
            active.Database.SetCommandTimeout(19);
            await using var transaction = await active.Database.BeginTransactionAsync(ct);
            await using var command = query.CreateDbCommand();
            Assert.Same(connection, command.Connection);
            Assert.Same(transaction.GetDbTransaction(), command.Transaction);
            Assert.Equal(19, command.CommandTimeout);
            Assert.Contains(command.Parameters.Cast<DbParameter>(), parameter => Equals(parameter.Value, name));
            Assert.DoesNotContain(name, command.CommandText, StringComparison.Ordinal);
            await using var reader = await command.ExecuteReaderAsync(ct);
            var names = new System.Collections.Generic.List<string>();
            while (await reader.ReadAsync(ct)) {
                names.Add(reader.GetString(reader.GetOrdinal(nameof(Student.FullName))));
            }
            Assert.Equal(new[] { name }, names);
            return names.Count;
        }
    }

    [Fact]
    public async Task UnsupportedShapes_ReturnNull_WithoutExecutingSql() {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = CreateServices(connection);
        new SchemataRepositoryBuilder(services).WithCountEstimates<EstimateContext>(QueryEstimateProvider.PostgreSql);
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<Student>>();
        var statements = 0;
        strdelegate_trace trace = (_, _) => statements++;
        raw.sqlite3_trace(connection.Handle, trace, null!);
        try {
            Assert.Null(await repository.EstimateCountAsync(q => q.Select(student => student.Grade)));
            Assert.Null(await repository.EstimateCountAsync(q => q.Distinct()));
            Assert.Null(await repository.EstimateCountAsync(q => q.GroupBy(student => student.Grade)));
            Assert.Null(await repository.EstimateCountAsync(q => q.Take(1)));
            Assert.Null(await repository.EstimateCountAsync(q => q.Skip(1)));
            Assert.Null(await repository.EstimateCountAsync(q => q.Union(q)));
            Assert.Null(await repository.EstimateCountAsync(q => q.Include("UnsupportedNavigation")));
            Assert.Equal(0, statements);
        } finally {
            raw.sqlite3_trace(connection.Handle, (strdelegate_trace)null!, null!);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_PropagatesBeforeDatabaseExecution(bool optIn) {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        var services = CreateServices(connection);
        if (optIn) {
            new SchemataRepositoryBuilder(services).WithCountEstimates<EstimateContext>(QueryEstimateProvider.SqlServer);
        }
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<Student>>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.EstimateCountAsync(q => q, cts.Token).AsTask());
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact]
    public async Task CustomEstimatorFailure_PropagatesWithoutExactFallback() {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        var estimator = new Mock<IEfCoreCountEstimator<EstimateContext>>(MockBehavior.Strict);
        var error = new InvalidOperationException("estimate failed");
        estimator.Setup(e => e.EstimateAsync(It.IsAny<EstimateContext>(), It.IsAny<IQueryable<Student>>(), default))
                 .Throws(error);
        var services = CreateServices(connection);
        services.AddScoped(_ => estimator.Object);
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<Student>>();

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => repository.EstimateCountAsync(q => q).AsTask());
        Assert.Same(error, actual);
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    private static ServiceCollection CreateServices(SqliteConnection connection) {
        var services = new ServiceCollection();
        services.AddDbContextFactory<EstimateContext>(options => options.UseSqlite(connection));
        services.AddRepository<Student, EfCoreRepository<EstimateContext, Student>>();
        return services;
    }

    public class EstimateContext(DbContextOptions<EstimateContext> options) : DbContext(options)
    {
        public DbSet<Student> Students => Set<Student>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) {
            modelBuilder.Entity<Student>().HasKey(student => student.Uid);
            modelBuilder.Entity<Student>().HasQueryFilter(student => student.Age >= 18);
        }
    }
}
