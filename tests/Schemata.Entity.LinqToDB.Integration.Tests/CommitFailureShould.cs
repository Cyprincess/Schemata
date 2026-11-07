using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.LinqToDB.Integration.Tests.Fixtures;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Caching.Skeleton;
using Schemata.Resource.Foundation;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Foundation.Commands;
using Moq;
using Schemata.Mapping.Skeleton;
using Xunit;

namespace Schemata.Entity.LinqToDB.Integration.Tests;

[Trait("Category", "Integration")]
public class CommitFailureShould : IAsyncLifetime
{
    private readonly IntegrationFixture _fixture = new();

    #region IAsyncLifetime Members

    public Task InitializeAsync() { return _fixture.InitializeAsync(); }

    public Task DisposeAsync() { return _fixture.DisposeAsync(); }

    #endregion

    [Fact]
    public async Task FailedInsert_RollsBackTransaction() {
        var existing = await SeedAsync("existing");

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                // LinqToDB executes inserts immediately, so the pre-check-bypassed duplicate fails
                // against the database during AddAsync; the actual SQL boundary classifies the
                // provider's unique violation as a consumer-facing ALREADY_EXISTS error.
                await Assert.ThrowsAsync<AlreadyExistsException>(() => repository.AddAsync(new() {
                    Uid      = existing,
                    FullName = "duplicate",
                    Name     = "duplicate",
                    Age      = 1,
                    Grade    = 1,
                }, default, new MutationOptions(IgnoreUniqueness: true)));

                // Scope disposal rolls back the implicit unit of work's transaction.
            }
        }

        {
            var (verifier, verifyScope) = _fixture.CreateScopeWithRepository();
            using (verifyScope) {
                var duplicates = await verifier.CountAsync<Student>(q => q.Where(s => s.Uid == existing));
                Assert.Equal(1, duplicates);
            }
        }
    }

    [Fact]
    public async Task LostAcknowledgement_PreservesOriginalFailure_AndSkipsRollbackSinks() {
        using var fixture = new FaultFixture(new() {
            FailCommitAfterSave = true,
            FailRollback        = true,
            FailDispose         = true,
        });
        var rolledBack = 0;

        var (repository, unit, scope) = fixture.CreateScope();
        using (scope) {
            repository.Join(unit);
            unit.AddRollbackSink(() => rolledBack++);
            await repository.AddAsync(new Student {
                Uid      = Guid.NewGuid(),
                FullName = "Durable Row",
                Name     = "durable",
                Age      = 20,
                Grade    = 1,
            });

            var error = await Assert.ThrowsAsync<AggregateException>(() => unit.CommitAsync());

            Assert.IsType<LostAcknowledgementException>(error.InnerExceptions[0]);
            Assert.Equal(["rollback failed", "dispose failed"], error.InnerExceptions.Skip(1).Select(e => e.Message));
        }

        Assert.Equal(0, rolledBack);
        Assert.Equal(["commit", "rollback", "dispose"], fixture.Settings.Events);
        Assert.Equal(1, await fixture.FreshCountAsync("durable"));
    }

    [Fact]
    public async Task ExplicitRollback_RunsRollbackSinkAfterConfirmedRollback() {
        using var fixture = new FaultFixture(new());

        var (repository, unit, scope) = fixture.CreateScope();
        using (scope) {
            repository.Join(unit);
            unit.AddRollbackSink(() => fixture.Settings.Events.Add("rollback-sink"));
            await repository.AddAsync(new Student {
                Uid      = Guid.NewGuid(),
                FullName = "Rollback Row",
                Name     = "rollback",
                Age      = 20,
                Grade    = 1,
            });

            await unit.RollbackAsync();
        }

        Assert.Equal(["rollback", "rollback-sink", "dispose"], fixture.Settings.Events);
        Assert.Equal(0, await fixture.FreshCountAsync("rollback"));
    }

    [Fact]
    public async Task PostCommitDisposalFailure_DoesNotRunRollbackSinks() {
        using var fixture = new FaultFixture(new() { FailDispose = true });
        var rolledBack = 0;
        var committed  = 0;

        var (repository, unit, scope) = fixture.CreateScope();
        using (scope) {
            repository.Join(unit);
            unit.AddRollbackSink(() => rolledBack++);
            unit.AddCommitSink(0, _ => {
                committed++;
                return Task.CompletedTask;
            });
            await repository.AddAsync(new Student {
                Uid      = Guid.NewGuid(),
                FullName = "Committed Row",
                Name     = "committed",
                Age      = 20,
                Grade    = 1,
            });

            var error = await Assert.ThrowsAsync<CleanupException>(() => unit.CommitAsync());
            Assert.Equal("dispose failed", error.Message);
        }

        Assert.Equal(0, rolledBack);
        Assert.Equal(1, committed);
        Assert.Equal(1, await fixture.FreshCountAsync("committed"));
    }

    [Fact]
    public async Task CommitSinkFailure_PrecedesDisposalFailure() {
        using var fixture = new FaultFixture(new() { FailDispose = true });
        var rolledBack = 0;

        var (repository, unit, scope) = fixture.CreateScope();
        using (scope) {
            repository.Join(unit);
            unit.AddRollbackSink(() => rolledBack++);
            unit.AddCommitSink(0, _ => {
                fixture.Settings.Events.Add("commit-sink");
                throw new CleanupException("commit-sink failed");
            });
            await repository.AddAsync(new Student {
                Uid      = Guid.NewGuid(),
                FullName = "Sink Row",
                Name     = "sink",
                Age      = 20,
                Grade    = 1,
            });

            var error = await Assert.ThrowsAsync<AggregateException>(() => unit.CommitAsync());

            Assert.Equal(["commit-sink failed", "dispose failed"], error.InnerExceptions.Select(e => e.Message));
        }

        Assert.Equal(0, rolledBack);
        Assert.Equal(["commit", "commit-sink", "dispose"], fixture.Settings.Events);
        Assert.Equal(1, await fixture.FreshCountAsync("sink"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_Disposal_Rollback_Does_Not_Publish_Release_Evidence(bool asynchronous) {
        using var fixture = new FaultFixture(new() { FailRollback = true, FailDispose = true });
        var (repository, unit, scope) = fixture.CreateScope();
        using (scope) {
            repository.Join(unit);
            var released = false;
            unit.AddRollbackSink(() => released = true);
            await repository.AddAsync(new Student { Uid = Guid.NewGuid(), FullName = "Pending", Name = "pending", Age = 20, Grade = 1 });

            var error = asynchronous
                ? await Assert.ThrowsAsync<AggregateException>(async () => await unit.DisposeAsync())
                : Assert.Throws<AggregateException>(() => unit.Dispose());

            Assert.Equal("rollback failed", error.InnerExceptions[0].Message);
            Assert.Equal("dispose failed", error.InnerExceptions[1].Message);
            Assert.False(released);
            Assert.Equal(new[] { "rollback", "dispose" }, fixture.Settings.Events.Take(2));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disposal_Rollback_Failure_Retains_Resource_Idempotency_Reservation(bool cancellation) {
        using var fixture = new FaultFixture(new() { FailRollback = true });
        var (repository, _, scope) = fixture.CreateScope();
        using (scope) {
            var services = new ServiceCollection();
            services.AddMemoryCacheProvider();
            services.AddSingleton(repository);
            services.AddScoped<IResourceMutation<Student>, ResourceMutation<Student>>();
            await using var cacheServices = services.BuildServiceProvider();
            var request = new CreateResourceRequest<Student, IdentifiedStudent, Student>(new() { RequestId = "unknown-write", Name = "pending" }, null);
            var advisor = new ResourceIdempotencyPipelineAdvisor<Student, IdentifiedStudent,
                CreateResourceRequest<Student, IdentifiedStudent, Student>, Student, CreateResultBase<Student>>(
                _ => "create", value => value.Request, _ => "students", (value, execution) => value.Dispatch = execution,
                detail => new() { Detail = detail }, result => result.Detail);
            var mapper = new Mock<ISimpleMapper>();
            mapper.Setup(value => value.Map<IdentifiedStudent, Student>(request.Request))
                .Returns(new Student { Uid = Guid.NewGuid(), Name = "pending", FullName = "Pending", Age = 20, Grade = 1 });
            mapper.Setup(value => value.Map<Student, Student>(It.IsAny<Student>()))
                .Throws(cancellation ? new OperationCanceledException() : new InvalidOperationException("projection failed"));
            var handler = new ResourceOperationHandler<Student, IdentifiedStudent, Student, Student>(cacheServices, repository, mapper.Object);
            var failure = await Assert.ThrowsAsync<AggregateException>(() => advisor.AdviseAsync(new AdviceContext(cacheServices), request,
                token => handler.CreateAsync(request.Request, null, new AdviceContext(cacheServices), token, request.Dispatch), CancellationToken.None));
            Assert.Equal(cancellation ? typeof(OperationCanceledException) : typeof(InvalidOperationException), failure.InnerExceptions[0].GetType());
            Assert.Equal("rollback failed", failure.InnerExceptions[1].Message);

            var dispatch = Assert.IsType<ResourceDispatchExecution>(request.Dispatch);
            Assert.Equal(ResourceDispatchOutcome.Reached, dispatch.Outcome);
            using var verification = cacheServices.CreateScope();
            var retained = await verification.ServiceProvider.GetRequiredService<ICacheProvider>().GetAsync(dispatch.Key);
            Assert.Equal(dispatch.Reservation, retained);
            Assert.Equal(0, await fixture.FreshCountAsync("pending"));
            Assert.Equal(1, fixture.Settings.Events.Count(value => value == "rollback"));
        }
    }

    private sealed class IdentifiedStudent : IRequestIdentification, ICanonicalName {
        public string? RequestId { get; set; }
        public string? Name { get; set; }
        public string? CanonicalName { get; set; }
    }

    private async Task<Guid> SeedAsync(string name) {
        var id = Guid.NewGuid();
        var (repository, scope) = _fixture.CreateScopeWithRepository();
        using (scope) {
            await repository.AddAsync(new() {
                                          Uid      = id,
                                          FullName = name,
                                          Name     = name,
                                          Age      = 20,
                                          Grade    = 1,
                                      });
            await repository.CommitAsync();
        }

        return id;
    }

    private sealed class FaultSettings
    {
        public bool FailCommitAfterSave { get; set; }
        public bool FailRollback        { get; set; }
        public bool FailDispose         { get; set; }

        public List<string> Events { get; } = [];
    }

    private sealed class LostAcknowledgementException : Exception
    {
    }

    private sealed class CleanupException(string message) : Exception(message)
    {
    }

    private sealed class FaultConnection(string connectionString, FaultSettings settings) : DbConnection
    {
        private readonly SqliteConnection _inner = new(connectionString);

        internal SqliteConnection Inner => _inner;

        [AllowNull]
        public override string ConnectionString { get => _inner.ConnectionString; set => _inner.ConnectionString = value; }
        public override string Database => _inner.Database;
        public override string DataSource => _inner.DataSource;
        public override string ServerVersion => _inner.ServerVersion;
        public override ConnectionState State => _inner.State;

        public override void ChangeDatabase(string databaseName) => _inner.ChangeDatabase(databaseName);
        public override void Close() => _inner.Close();
        public override void Open() => _inner.Open();
        public override Task OpenAsync(CancellationToken cancellationToken) => _inner.OpenAsync(cancellationToken);

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) {
            return new FaultDbTransaction(this, (SqliteTransaction)_inner.BeginTransaction(isolationLevel), settings);
        }

        protected override DbCommand CreateDbCommand() => new FaultDbCommand(_inner.CreateCommand());

        protected override void Dispose(bool disposing) {
            if (disposing) {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync() {
            await _inner.DisposeAsync();
            GC.SuppressFinalize(this);
        }
    }

    private sealed class FaultDbCommand(SqliteCommand inner) : DbCommand
    {
        [AllowNull]
        public override string CommandText { get => inner.CommandText; set => inner.CommandText = value; }
        public override int CommandTimeout { get => inner.CommandTimeout; set => inner.CommandTimeout = value; }
        public override CommandType CommandType { get => inner.CommandType; set => inner.CommandType = value; }
        public override bool DesignTimeVisible { get => inner.DesignTimeVisible; set => inner.DesignTimeVisible = value; }
        public override UpdateRowSource UpdatedRowSource { get => inner.UpdatedRowSource; set => inner.UpdatedRowSource = value; }

        protected override DbConnection? DbConnection
        {
            get => inner.Connection;
            set => inner.Connection = value switch {
                FaultConnection connection => connection.Inner,
                SqliteConnection connection => connection,
                null => null,
                _ => throw new ArgumentException("Unsupported connection type.", nameof(value)),
            };
        }

        protected override DbParameterCollection DbParameterCollection => inner.Parameters;

        protected override DbTransaction? DbTransaction
        {
            get => inner.Transaction;
            set => inner.Transaction = value switch {
                FaultDbTransaction transaction => transaction.Inner,
                SqliteTransaction transaction => transaction,
                null => null,
                _ => throw new ArgumentException("Unsupported transaction type.", nameof(value)),
            };
        }

        public override void Cancel() => inner.Cancel();
        public override int ExecuteNonQuery() => inner.ExecuteNonQuery();
        public override object? ExecuteScalar() => inner.ExecuteScalar();
        public override void Prepare() => inner.Prepare();
        public override Task PrepareAsync(CancellationToken cancellationToken = default) => inner.PrepareAsync(cancellationToken);
        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken) => inner.ExecuteNonQueryAsync(cancellationToken);
        public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken) => inner.ExecuteScalarAsync(cancellationToken);

        protected override DbParameter CreateDbParameter() => inner.CreateParameter();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => inner.ExecuteReader(behavior);

        protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(
            CommandBehavior behavior,
            CancellationToken cancellationToken
        ) {
            return await inner.ExecuteReaderAsync(behavior, cancellationToken);
        }

        protected override void Dispose(bool disposing) {
            if (disposing) {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync() {
            await inner.DisposeAsync();
            GC.SuppressFinalize(this);
        }
    }

    private sealed class FaultDbTransaction(
        FaultConnection   owner,
        SqliteTransaction inner,
        FaultSettings     settings) : DbTransaction
    {
        private bool _disposed;

        internal SqliteTransaction Inner => inner;

        protected override DbConnection DbConnection => owner;
        public override IsolationLevel IsolationLevel => inner.IsolationLevel;

        public override void Commit() {
            settings.Events.Add("commit");
            inner.Commit();
            ThrowLostAcknowledgement();
        }

        public override async Task CommitAsync(CancellationToken cancellationToken = default) {
            settings.Events.Add("commit");
            await inner.CommitAsync(cancellationToken);
            ThrowLostAcknowledgement();
        }

        public override void Rollback() {
            settings.Events.Add("rollback");
            if (settings.FailRollback) {
                throw new CleanupException("rollback failed");
            }

            inner.Rollback();
        }

        public override async Task RollbackAsync(CancellationToken cancellationToken = default) {
            settings.Events.Add("rollback");
            if (settings.FailRollback) {
                throw new CleanupException("rollback failed");
            }

            await inner.RollbackAsync(cancellationToken);
        }

        protected override void Dispose(bool disposing) {
            if (disposing) {
                DisposeTransaction();
            }

            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync() {
            try {
                DisposeTransaction();
                GC.SuppressFinalize(this);
                return ValueTask.CompletedTask;
            } catch (Exception ex) {
                return ValueTask.FromException(ex);
            }
        }

        private void ThrowLostAcknowledgement() {
            if (settings.FailCommitAfterSave) {
                throw new LostAcknowledgementException();
            }
        }

        private void DisposeTransaction() {
            if (_disposed) {
                return;
            }

            _disposed = true;
            inner.Dispose();
        }
    }

    private sealed class FaultDataConnection(DataOptions options, FaultSettings settings) : TestDataConnection(options)
    {
        public override void DisposeTransaction() {
            settings.Events.Add("dispose");
            if (settings.FailDispose) {
                throw new CleanupException("dispose failed");
            }

            base.DisposeTransaction();
        }

        public override Task DisposeTransactionAsync() {
            settings.Events.Add("dispose");
            if (settings.FailDispose) {
                throw new CleanupException("dispose failed");
            }

            return base.DisposeTransactionAsync();
        }
    }

    private sealed class FaultFixture : IDisposable
    {
        private readonly string          _path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():n}.db");
        private readonly DataOptions     _readOptions;
        private readonly ServiceProvider _root;

        public FaultFixture(FaultSettings settings) {
            Settings = settings;
            var schema = new MappingSchema();
            schema.AddMetadataReader(new SystemComponentModelDataAnnotationsSchemaAttributeReader());

            var connectionString = $"Data Source={_path}";
            var faultOptions = new DataOptions()
                .UseSQLite(connectionString)
                .UseMappingSchema(schema)
                .UseConnectionFactory(_ => new FaultConnection(connectionString, Settings));
            _readOptions = new DataOptions().UseSQLite(connectionString).UseMappingSchema(schema);

            var services = new ServiceCollection();
            services.TryAddScoped<TestDataConnection>(_ => new FaultDataConnection(faultOptions, Settings));
            services.TryAddSingleton<Func<TestDataConnection>>(_ => () => new FaultDataConnection(faultOptions, Settings));
            services.AddRepository<Student, LinqToDbRepository<TestDataConnection, Student>>();
            services.AddScoped<IUnitOfWork<TestDataConnection>, LinqToDbUnitOfWork<TestDataConnection>>();
            _root = services.BuildServiceProvider();

            using var scope      = _root.CreateScope();
            using var connection = scope.ServiceProvider.GetRequiredService<TestDataConnection>();
            connection.CreateTableWithIndexes<Student>();
        }

        public FaultSettings Settings { get; }

        public (IRepository<Student> Repository, IUnitOfWork<TestDataConnection> Unit, IServiceScope Scope) CreateScope() {
            var scope = _root.CreateScope();
            return (
                scope.ServiceProvider.GetRequiredService<IRepository<Student>>(),
                scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDataConnection>>(),
                scope);
        }

        public Task<int> FreshCountAsync(string name) {
            using var connection = new TestDataConnection(_readOptions);
            return Task.FromResult(connection.Students.Count(s => s.Name == name));
        }

        public void Dispose() {
            _root.Dispose();
            SqliteConnection.ClearAllPools();
            File.Delete(_path);
        }
    }
}
