using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.EntityFrameworkCore.Integration.Tests.Fixtures;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Xunit;

namespace Schemata.Entity.EntityFrameworkCore.Integration.Tests;

[Trait("Category", "Integration")]
public class RepositoryCrudShould : IAsyncLifetime
{
    private readonly IntegrationFixture _fixture = new();

    #region IAsyncLifetime Members

    public Task InitializeAsync() { return _fixture.InitializeAsync(); }

    public Task DisposeAsync() { return _fixture.DisposeAsync(); }

    #endregion

    [Fact]
    public async Task Add_ThenCommit_PersistsEntity() {
        Guid uid;
        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var entity = new Student {
                    FullName = "Alice",
                    Age      = 18,
                    Grade    = 1,
                    Name     = "alice",
                };
                await repository.AddAsync(entity);
                await repository.CommitAsync();
                uid = entity.Uid;
                Assert.NotEqual(Guid.Empty, uid);
            }
        }

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var found = await repository.FindAsync([uid]);
                Assert.NotNull(found);
                Assert.Equal("Alice", found.FullName);
            }
        }
    }

    [Fact]
    public async Task Add_ThenUpdateBeforeCommit_PersistsFinalValuesAndNotifiesOnlyAdded() {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new Mock<TimeProvider>();
        clock.Setup(time => time.GetUtcNow()).Returns(() => now);
        var commits = new List<CommitChanges<Student>>();
        var observer = new Mock<IRepositoryCommittedAdvisor<Student>>();
        observer.Setup(advisor => advisor.AdviseAsync(
                    It.IsAny<AdviceContext>(), It.IsAny<IRepository<Student>>(),
                    It.IsAny<CommitChanges<Student>>(), It.IsAny<CancellationToken>()))
                .Callback<AdviceContext, IRepository<Student>, CommitChanges<Student>, CancellationToken>(
                    (_, _, changes, _) => commits.Add(changes))
                .ReturnsAsync(AdviseResult.Continue);
        var services = new ServiceCollection();
        services.AddDbContextFactory<TestDbContext>(options => options.UseSqlite(connection)
            .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        services.AddRepository<Student, EfCoreRepository<TestDbContext, Student>>();
        services.AddSingleton(clock.Object);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRepositoryAddAdvisor<Student>>(new StudentNameAdvisor()));
        services.TryAddEnumerable(ServiceDescriptor.Singleton(observer.Object));
        await using var provider = services.BuildServiceProvider();
        await using (var db = await provider.GetRequiredService<IDbContextFactory<TestDbContext>>().CreateDbContextAsync()) {
            await db.Database.EnsureCreatedAsync();
        }

        Guid uid;
        Guid timestamp;
        using (var scope = provider.CreateScope()) {
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<Student>>();
            await using var uow = repository.Begin();
            var entity = new Student { FullName = "Initial", Age = 18, Grade = 1 };
            await repository.AddAsync(entity);
            Assert.Equal("students/advisor-student", entity.CanonicalName);
            uid = entity.Uid;
            entity.FullName = "Final";
            entity.Grade = 2;
            now = now.AddHours(1);
            await repository.UpdateAsync(entity);
            await uow.CommitAsync();
        }

        var added = Assert.Single(commits);
        Assert.Equal(uid, Assert.Single(added.Added).Uid);
        Assert.Equal("Final", added.Added[0].FullName);
        Assert.Empty(added.Updated);
        Assert.Empty(added.Removed);
        using (var scope = provider.CreateScope()) {
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<Student>>();
            var entity = await repository.FindAsync([uid]);
            Assert.NotNull(entity);
            Assert.Equal("Final", entity.FullName);
            Assert.Equal(2, entity.Grade);
            Assert.Equal("students/advisor-student", entity.CanonicalName);
            Assert.Equal(now.UtcDateTime, entity.UpdateTime);
            timestamp = entity.Timestamp;
            entity.FullName = "Existing updated";
            now = now.AddHours(1);
            await repository.UpdateAsync(entity);
            await repository.CommitAsync();
        }

        Assert.Equal(2, commits.Count);
        var updated = commits[1];
        Assert.Empty(updated.Added);
        Assert.Equal(uid, Assert.Single(updated.Updated).Uid);
        Assert.Equal("Existing updated", updated.Updated[0].FullName);
        Assert.Empty(updated.Removed);
        using (var scope = provider.CreateScope()) {
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<Student>>();
            var entity = await repository.FindAsync([uid]);
            Assert.NotNull(entity);
            Assert.Equal("Existing updated", entity.FullName);
            Assert.Equal(now.UtcDateTime, entity.UpdateTime);
            Assert.NotEqual(timestamp, entity.Timestamp);
        }
    }

    [Fact]
    public async Task Update_ThenCommit_ModifiesEntity() {
        Guid uid;
        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var entity = new Student {
                    FullName = "Bob",
                    Age      = 19,
                    Grade    = 2,
                    Name     = "bob",
                };
                await repository.AddAsync(entity);
                await repository.CommitAsync();
                uid = entity.Uid;
            }
        }

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var entity = await repository.FindAsync([uid]);
                Assert.NotNull(entity);
                entity.FullName = "Bob Updated";
                await repository.UpdateAsync(entity);
                await repository.CommitAsync();
            }
        }

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var entity = await repository.FindAsync([uid]);
                Assert.Equal("Bob Updated", entity?.FullName);
            }
        }
    }

    [Fact]
    public async Task Remove_ThenCommit_DeletesEntity() {
        Guid uid;
        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var entity = new Student {
                    FullName = "Charlie",
                    Age      = 20,
                    Grade    = 3,
                    Name     = "charlie",
                };
                await repository.AddAsync(entity);
                await repository.CommitAsync();
                uid = entity.Uid;
            }
        }

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var entity = await repository.FindAsync([uid]);
                Assert.NotNull(entity);
                await repository.RemoveAsync(entity);
                await repository.CommitAsync();
            }
        }

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var entity = await repository.FindAsync([uid]);
                Assert.Null(entity);
            }
        }
    }

    [Fact]
    public async Task Add_DuplicateKey_ThrowsAlreadyExists() {
        var uid = Guid.NewGuid();
        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                await repository.AddAsync(new() {
                                              Uid = uid, FullName = "Original", Name = "original",
                                          });
                await repository.CommitAsync();
            }
        }

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                await Assert.ThrowsAsync<AlreadyExistsException>(() => repository.AddAsync(
                                                                     new() {
                                                                         Uid      = uid,
                                                                         FullName = "Duplicate",
                                                                         Name     = "duplicate",
                                                                     }));
            }
        }
    }

    [Fact]
    public async Task Add_WithoutCommit_NotVisibleInNewScope() {
        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var entity = new Student {
                    FullName = "Dave",
                    Age      = 21,
                    Grade    = 4,
                    Name     = "dave",
                };
                await repository.AddAsync(entity);
                // Scope disposal leaves the pending insert uncommitted.
            }
        }

        {
            var (repository, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var found = await repository.FirstOrDefaultAsync(q => q.Where(s => s.FullName == "Dave"));
                Assert.Null(found);
            }
        }
    }

    private sealed class StudentNameAdvisor : IRepositoryAddAdvisor<Student>
    {
        public int Order => AdviceAddCanonicalName.DefaultOrder - 1;

        public Task<AdviseResult> AdviseAsync(
            AdviceContext ctx, IRepository<Student> repository, Student entity, CancellationToken ct
        ) {
            entity.Name = "advisor-student";
            return Task.FromResult(AdviseResult.Continue);
        }
    }
}
