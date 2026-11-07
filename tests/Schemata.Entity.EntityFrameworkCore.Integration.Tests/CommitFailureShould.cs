using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.EntityFrameworkCore.Integration.Tests.Fixtures;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Xunit;

namespace Schemata.Entity.EntityFrameworkCore.Integration.Tests;

[Trait("Category", "Integration")]
public class CommitFailureShould : IAsyncLifetime
{
    private readonly IntegrationFixture _fixture = new();

    #region IAsyncLifetime Members

    public Task InitializeAsync() { return _fixture.InitializeAsync(); }

    public Task DisposeAsync() { return _fixture.DisposeAsync(); }

    #endregion

    [Fact]
    public async Task CommitFailure_RollsBackPendingInsert() {
        var existing   = await SeedAsync("existing");
        var rolledBack = 0;

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                await using var unit = repository.Begin();
                unit.AddRollbackSink(() => rolledBack++);

                // The duplicate key reaches SQLite, whose unique violation confirms that the
                // save transaction did not commit.
                await repository.AddAsync(new() {
                                              Uid      = existing,
                                              FullName = "duplicate",
                                              Name     = "duplicate",
                                              Age      = 1,
                                              Grade    = 1,
                                          },
                                          default,
                                          new MutationOptions(IgnoreUniqueness: true));

                await Assert.ThrowsAsync<AlreadyExistsException>(() => unit.CommitAsync());
            }
        }

        Assert.Equal(1, rolledBack);

        // The failed commit must roll back its transaction: a fresh scope sees the seeded row.
        {
            var (verifier, verifyScope) = _fixture.CreateScopeWithRepository();
            using (verifyScope) {
                var count = await verifier.CountAsync<Student>(q => q.Where(s => s.Uid == existing));
                Assert.Equal(1, count);
            }
        }
    }

    [Fact]
    public async Task Lost_Acknowledgement_Does_Not_Run_Rollback_Sinks_Or_Release_The_Row() {
        using var fixture = new FaultFixture(SaveFailure.AfterSave);
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
                FullName = "Durable Row",
                Name     = "durable",
                Age      = 20,
                Grade    = 1,
            });

            await Assert.ThrowsAsync<LostAcknowledgementException>(() => unit.CommitAsync());
        }

        Assert.Equal(0, rolledBack);
        Assert.Equal(0, committed);
        Assert.Equal(1, await fixture.FreshCountAsync("durable"));
    }

    [Fact]
    public async Task SaveFailure_WithoutNoCommitEvidence_DoesNotRunRollbackSinks() {
        using var fixture = new FaultFixture(SaveFailure.BeforeSave);
        var rolledBack = 0;

        var (repository, unit, scope) = fixture.CreateScope();
        using (scope) {
            repository.Join(unit);
            unit.AddRollbackSink(() => rolledBack++);
            await repository.AddAsync(new Student {
                Uid      = Guid.NewGuid(),
                FullName = "Unknown Row",
                Name     = "unknown",
                Age      = 20,
                Grade    = 1,
            });

            await Assert.ThrowsAsync<InvalidOperationException>(() => unit.CommitAsync());
        }

        Assert.Equal(0, rolledBack);
        Assert.Equal(0, await fixture.FreshCountAsync("unknown"));
    }

    [Fact]
    public async Task LostAcknowledgement_RestoresCallerStamp_WhileKeepingCommittedRow() {
        using var fixture = new FaultFixture(SaveFailure.None);
        var id = Guid.NewGuid();

        {
            var (repository, unit, scope) = fixture.CreateScope();
            using (scope) {
                repository.Join(unit);
                await repository.AddAsync(new Student {
                    Uid      = id,
                    FullName = "Stamp Target",
                    Name     = "stamp-target",
                    Age      = 1,
                    Grade    = 1,
                });
                await unit.CommitAsync();
            }
        }

        fixture.Settings.Failure = SaveFailure.AfterSave;
        var original   = Guid.Empty;
        var rolledBack = 0;

        {
            var (repository, unit, scope) = fixture.CreateScope();
            using (scope) {
                repository.Join(unit);
                var entity = await repository.SingleOrDefaultAsync(q => q.Where(s => s.Uid == id));
                Assert.NotNull(entity);
                original = entity.Timestamp;
                entity.Age = 2;

                unit.AddRollbackSink(() => rolledBack++);
                await repository.UpdateAsync(entity);
                await Assert.ThrowsAsync<LostAcknowledgementException>(() => unit.CommitAsync());

                Assert.Equal(original, entity.Timestamp);
            }
        }

        Assert.Equal(0, rolledBack);
        var persisted = await fixture.FreshStudentAsync(id);
        Assert.NotNull(persisted);
        Assert.Equal(2, persisted.Age);
        Assert.NotEqual(original, persisted.Timestamp);
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

    private enum SaveFailure
    {
        None,
        BeforeSave,
        AfterSave,
    }

    private sealed class SaveSettings
    {
        public SaveFailure Failure { get; set; }
    }

    private sealed class LostAcknowledgementException : Exception
    {
    }

    private sealed class FaultDbContext(
        DbContextOptions<FaultDbContext> options,
        SaveSettings                   settings) : DbContext(options)
    {
        public DbSet<Student> Students => Set<Student>();

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) {
            if (settings.Failure == SaveFailure.BeforeSave) {
                throw new InvalidOperationException("Save failed without provider commit evidence.");
            }

            var saved = await base.SaveChangesAsync(cancellationToken);
            if (settings.Failure == SaveFailure.AfterSave) {
                throw new LostAcknowledgementException();
            }

            return saved;
        }
    }

    private sealed class FaultFixture : IDisposable
    {
        private readonly string                            _path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():n}.db");
        private readonly DbContextOptions<FaultDbContext>  _options;
        private readonly ServiceProvider                   _root;

        public FaultFixture(SaveFailure failure) {
            Settings = new() { Failure = failure };
            _options = new DbContextOptionsBuilder<FaultDbContext>()
                .UseSqlite($"Data Source={_path}")
                .ReplaceService<IModelCustomizer, SchemataModelCustomizer>()
                .Options;

            var services = new ServiceCollection();
            services.AddSingleton(Settings);
            services.AddDbContextFactory<FaultDbContext>(options => options
                .UseSqlite($"Data Source={_path}")
                .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
            services.AddRepository<Student, EfCoreRepository<FaultDbContext, Student>>();
            services.AddScoped<IUnitOfWork<FaultDbContext>, EfCoreUnitOfWork<FaultDbContext>>();
            _root = services.BuildServiceProvider();

            using var scope   = _root.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<FaultDbContext>();
            context.Database.EnsureCreated();
        }

        public SaveSettings Settings { get; }

        public (IRepository<Student> Repository, IUnitOfWork<FaultDbContext> Unit, IServiceScope Scope) CreateScope() {
            var scope = _root.CreateScope();
            return (
                scope.ServiceProvider.GetRequiredService<IRepository<Student>>(),
                scope.ServiceProvider.GetRequiredService<IUnitOfWork<FaultDbContext>>(),
                scope);
        }

        public async Task<int> FreshCountAsync(string name) {
            await using var context = new FaultDbContext(_options, new());
            return await context.Students.CountAsync(s => s.Name == name);
        }

        public async Task<Student?> FreshStudentAsync(Guid id) {
            await using var context = new FaultDbContext(_options, new());
            return await context.Students.SingleOrDefaultAsync(s => s.Uid == id);
        }

        public void Dispose() {
            _root.Dispose();
            SqliteConnection.ClearAllPools();
            File.Delete(_path);
        }
    }
}
