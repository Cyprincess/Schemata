using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Entity.LinqToDB.Integration.Tests.Fixtures;
using Schemata.Entity.Repository;
using Xunit;

namespace Schemata.Entity.LinqToDB.Integration.Tests;

[Trait("Category", "Integration")]
public class UnitOfWorkShould : IAsyncLifetime
{
    private readonly IntegrationFixture _fixture = new();

    #region IAsyncLifetime Members

    public Task InitializeAsync() { return _fixture.InitializeAsync(); }

    public Task DisposeAsync() { return _fixture.DisposeAsync(); }

    #endregion

    [Trait("Layer", "Integration")]
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ShareWriteTransaction_UsesOwnerCommitOrRollback(bool commit) {
        var (students, courses, _, scope) = _fixture.CreateScopeWithUoW();
        using (scope) {
            await using var transaction = students.Begin();
            courses.Join(transaction);
            courses.Join(transaction);
            await students.AddAsync(new() { Uid = Guid.NewGuid(), Name = "shared-owner", FullName = "Owner", Age = 20, Grade = 1 });
            await courses.AddAsync(new() { Uid = Guid.NewGuid(), Name = "shared-child", Title = "Child", Credits = 3 });
            await Assert.ThrowsAsync<InvalidOperationException>(() => courses.CommitAsync());
            if (commit) await transaction.CommitAsync();
            else await transaction.RollbackAsync();
        }

        var (verifyStudents, studentScope) = _fixture.CreateScopeWithRepository();
        using (studentScope) {
            Assert.Equal(commit ? 1 : 0, await verifyStudents.CountAsync(q => q.Where(s => s.Name == "shared-owner")));
        }
        var (verifyCourses, courseScope) = _fixture.CreateScopeWithCourseRepository();
        using (courseScope) {
            Assert.Equal(commit ? 1 : 0, await verifyCourses.CountAsync(q => q.Where(c => c.Name == "shared-child")));
        }
    }

    [Trait("Layer", "Integration")]
    [Fact]
    public async Task Begin_PreservesExternalCommitOwnership() {
        var (students, courses, transaction, scope) = _fixture.CreateScopeWithUoW();
        using (scope) {
            students.Join(transaction);
            courses.Join(transaction);
            await courses.AddAsync(new() { Uid = Guid.NewGuid(), Name = "external-child", Title = "External", Credits = 2 });
            await Assert.ThrowsAsync<InvalidOperationException>(() => students.CommitAsync());
            await transaction.RollbackAsync();
            Assert.Throws<InvalidOperationException>(() => students.Begin());
        }
        var (verify, verifyScope) = _fixture.CreateScopeWithCourseRepository();
        using (verifyScope) {
            Assert.Equal(0, await verify.CountAsync(q => q.Where(c => c.Name == "external-child")));
        }
    }

    [Fact]
    public async Task CommitAsync_CommitsMultipleOperations() {
        {
            var (repo, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDataConnection>>();
                repo.Join(uow);
                await repo.AddAsync(new() {
                                        Uid      = Guid.NewGuid(),
                                        FullName = "UoW-Alice",
                                        Age      = 18,
                                        Grade    = 1,
                                        Name     = "uow-alice",
                                    });
                await repo.AddAsync(new() {
                                        Uid      = Guid.NewGuid(),
                                        FullName = "UoW-Bob",
                                        Age      = 19,
                                        Grade    = 2,
                                        Name     = "uow-bob",
                                    });
                await uow.CommitAsync();
            }
        }

        {
            var (repo, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var count = await repo.CountAsync(q => q.Where(s => s.Name!.StartsWith("uow-")));
                Assert.Equal(2, count);
            }
        }
    }

    [Fact]
    public async Task RollbackAsync_RollsBackChanges() {
        {
            var (repo, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDataConnection>>();
                repo.Join(uow);
                await repo.AddAsync(new() {
                                        Uid      = Guid.NewGuid(),
                                        FullName = "Rollback-Alice",
                                        Age      = 18,
                                        Grade    = 1,
                                        Name     = "rollback-alice",
                                    });
                await uow.RollbackAsync();
            }
        }

        {
            var (repo, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var found = await repo.FirstOrDefaultAsync(q => q.Where(s => s.Name == "rollback-alice"));
                Assert.Null(found);
            }
        }
    }

    [Fact]
    public async Task Dispose_WithoutCommit_RollsBack() {
        {
            var (repo, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDataConnection>>();
                repo.Join(uow);
                await repo.AddAsync(new() {
                                        Uid      = Guid.NewGuid(),
                                        FullName = "Dispose-Alice",
                                        Age      = 18,
                                        Grade    = 1,
                                        Name     = "dispose-alice",
                                    });
                // Scope disposal rolls back the enlisted unit of work.
            }
        }

        {
            var (repo, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var found = await repo.FirstOrDefaultAsync(q => q.Where(s => s.Name == "dispose-alice"));
                Assert.Null(found);
            }
        }
    }

    [Fact]
    public async Task CommitAsync_ThrowsWhenRepositoryIsEnlisted() {
        var (repo, _, uow, scope) = _fixture.CreateScopeWithUoW();
        using (scope) {
            repo.Join(uow);
            await repo.AddAsync(new() {
                                    Uid      = Guid.NewGuid(),
                                    FullName = "Enlisted",
                                    Age      = 18,
                                    Grade    = 1,
                                    Name     = "enlisted",
                                });
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await repo.CommitAsync());
            await uow.CommitAsync();
        }
    }

    [Fact]
    public async Task CrossRepository_SharesTransaction() {
        {
            var (studentRepo, courseRepo, uow, scope) = _fixture.CreateScopeWithUoW();
            using (scope) {
                studentRepo.Join(uow);
                courseRepo.Join(uow);
                await studentRepo.AddAsync(new() {
                                               Uid      = Guid.NewGuid(),
                                               FullName = "Cross-Alice",
                                               Age      = 18,
                                               Grade    = 1,
                                               Name     = "cross-alice",
                                           });
                await courseRepo.AddAsync(new() {
                                              Uid     = Guid.NewGuid(),
                                              Title   = "Cross-Course",
                                              Credits = 3,
                                              Name    = "cross-course",
                                          });
                await uow.CommitAsync();
            }
        }

        {
            var (studentRepo, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var student = await studentRepo.FirstOrDefaultAsync(q => q.Where(s => s.Name == "cross-alice"));
                Assert.NotNull(student);
            }
        }

        {
            var (courseRepo, scope) = _fixture.CreateScopeWithCourseRepository();
            using (scope) {
                var course = await courseRepo.FirstOrDefaultAsync(q => q.Where(c => c.Name == "cross-course"));
                Assert.NotNull(course);
            }
        }
    }

    [Fact]
    public async Task Standalone_ReadsSeeOwnUncommittedWrites() {
        // LinqToDB's standalone path executes mutations immediately inside the lazy
        // transaction, so subsequent reads on the same repository observe the
        // pending row (read-your-own-writes within the active transaction).
        var (repo, scope) = _fixture.CreateScopeWithRepository();
        using (scope) {
            await repo.AddAsync(new() {
                                    Uid      = Guid.NewGuid(),
                                    FullName = "Self-Read",
                                    Age      = 18,
                                    Grade    = 1,
                                    Name     = "self-read",
                                });

            var foundBefore = await repo.FirstOrDefaultAsync(q => q.Where(s => s.Name == "self-read"));
            Assert.NotNull(foundBefore);

            await repo.CommitAsync();

            var foundAfter = await repo.FirstOrDefaultAsync(q => q.Where(s => s.Name == "self-read"));
            Assert.NotNull(foundAfter);
        }
    }

    [Fact]
    public async Task Join_AfterUncommittedWork_ThrowsInvalidOperation() {
        var (repo, _, uow, scope) = _fixture.CreateScopeWithUoW();
        using (scope) {
            await repo.AddAsync(new() {
                                    Uid      = Guid.NewGuid(),
                                    FullName = "Uncommitted",
                                    Age      = 18,
                                    Grade    = 1,
                                    Name     = "uncommitted-join",
                                });

            Assert.Throws<InvalidOperationException>(() => repo.Join(uow));

            await repo.CommitAsync();
        }
    }

    [Fact]
    public async Task Read_ThenJoin_RebindsTableToUnitOfWorkContext() {
        var (repo, _, uow, scope) = _fixture.CreateScopeWithUoW();
        using (scope) {
            // The first read caches the table against the repository's standalone context.
            await repo.FirstOrDefaultAsync(q => q.Where(s => s.Name == "rebind"));

            // Joining disposes that context; the cached table must be dropped so subsequent reads
            // bind to the unit of work's connection.
            repo.Join(uow);
            await repo.AddAsync(new() {
                                    Uid      = Guid.NewGuid(),
                                    FullName = "Rebind",
                                    Age      = 18,
                                    Grade    = 1,
                                    Name     = "rebind",
                                });

            var found = await repo.FirstOrDefaultAsync(q => q.Where(s => s.Name == "rebind"));
            Assert.NotNull(found);

            await uow.CommitAsync();
        }
    }

    [Fact]
    public async Task CommitAsync_Twice_IsANoOpOnItsOwnUnitOfWork() {
        var (repo, scope) = _fixture.CreateScopeWithRepository();
        using (scope) {
            await repo.AddAsync(new() {
                                    Uid      = Guid.NewGuid(),
                                    FullName = "Double-Commit",
                                    Age      = 18,
                                    Grade    = 1,
                                    Name     = "double-commit",
                                });
            await repo.CommitAsync();
            await repo.CommitAsync();

            Assert.Equal(1, await repo.CountAsync(q => q.Where(s => s.Name == "double-commit")));
        }
    }

    [Fact]
    public async Task WriteAfterCommit_StagesIntoAFreshUnitOfWork() {
        {
            var (repo, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                await repo.AddAsync(new() {
                                        Uid      = Guid.NewGuid(),
                                        FullName = "Before-Commit",
                                        Age      = 18,
                                        Grade    = 1,
                                        Name     = "before-commit",
                                    });
                await repo.CommitAsync();

                // The repository reopened its own unit of work, so the second write stages into a new
                // one and stays uncommitted until its own CommitAsync.
                await repo.AddAsync(new() {
                    Uid      = Guid.NewGuid(),
                    FullName = "After-Commit",
                    Age      = 1,
                    Grade    = 1,
                    Name     = "after-commit-canary",
                });
            }
        }

        {
            var (verifier, verifyScope) = _fixture.CreateScopeWithRepository();
            using (verifyScope) {
                Assert.Equal(1, await verifier.CountAsync(q => q.Where(s => s.Name == "before-commit")));
                var found = await verifier.FirstOrDefaultAsync(q => q.Where(s => s.Name == "after-commit-canary"));
                Assert.Null(found);
            }
        }
    }

    [Fact]
    public async Task CommitAsync_AfterCompleted_ThrowsInvalidOperation() {
        var (_, _, uow, scope) = _fixture.CreateScopeWithUoW();
        using (scope) {
            await uow.CommitAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await uow.CommitAsync());
        }
    }

    [Trait("Layer", "Integration")]
    [Fact]
    public async Task JoinedPureRead_CommitCompletesEnlistment_AndRejectsFurtherUse() {
        var (students, _, transaction, scope) = _fixture.CreateScopeWithUoW();
        using (scope) {
            students.Join(transaction);

            // Pure read on the joined repository: no write stages, so no type-level committed
            // notification is enlisted, but the outer commit must still complete the enlistment.
            _ = await students.CountAsync(q => q);

            await transaction.CommitAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(async () => await students.CountAsync(q => q));
            Assert.Throws<InvalidOperationException>(() => students.Begin());
        }

        // A fresh scope resolves a repository that reads normally.
        var (fresh, freshScope) = _fixture.CreateScopeWithRepository();
        using (freshScope) {
            _ = await fresh.CountAsync(q => q);
        }
    }

    [Trait("Layer", "Integration")]
    [Fact]
    public async Task SavePreparation_RunsAtRegistration_ProjectionPersists() {
        var (seed, seedScope) = _fixture.CreateScopeWithRepository();
        using (seedScope) {
            await seed.AddAsync(new() { FullName = "Prep Target", Age = 1, Grade = 1, Name = "prep-target" });
            await seed.CommitAsync();
        }

        var original = Guid.Empty;
        var observed = Guid.Empty;
        var ran      = false;
        var (students, _, transaction, scope) = _fixture.CreateScopeWithUoW();
        using (scope) {
            students.Join(transaction);
            var entity = await students.FirstOrDefaultAsync(q => q.Where(s => s.Name == "prep-target"));
            Assert.NotNull(entity);
            original = entity.Timestamp;

            entity.Age = 2;
            await students.UpdateAsync(entity);

            // Immediate-execution provider: the staged write already ran inside the transaction,
            // so the preparation executes synchronously at registration and observes the final
            // stamp; the dependent write below persists the projected value.
            transaction.AddSavePreparation(() => {
                ran           = true;
                observed      = entity.Timestamp;
                entity.Grade  = 9;
            });
            Assert.True(ran);
            Assert.NotEqual(original, observed);

            await students.UpdateAsync(entity);
            await transaction.CommitAsync();
        }

        var (verifier, verifyScope) = _fixture.CreateScopeWithRepository();
        using (verifyScope) {
            var persisted = await verifier.FirstOrDefaultAsync(q => q.Where(s => s.Name == "prep-target"));
            Assert.NotNull(persisted);
            Assert.Equal(2, persisted.Age);
            Assert.Equal(9, persisted.Grade);
        }
    }

    [Trait("Layer", "Integration")]
    [Fact]
    public async Task SavePreparation_Throws_RegistrationFails_DisposalRollsBack() {
        var (seed, seedScope) = _fixture.CreateScopeWithRepository();
        using (seedScope) {
            await seed.AddAsync(new() { FullName = "Prep Fail", Age = 1, Grade = 1, Name = "prep-fail" });
            await seed.CommitAsync();
        }

        var (students, _, transaction, scope) = _fixture.CreateScopeWithUoW();
        using (scope) {
            students.Join(transaction);
            var entity = await students.FirstOrDefaultAsync(q => q.Where(s => s.Name == "prep-fail"));
            Assert.NotNull(entity);

            entity.Age = 3;
            await students.UpdateAsync(entity);

            // The preparation runs synchronously at registration, so the failure surfaces here;
            // disposing the uncommitted unit of work rolls the staged update back.
            Assert.Throws<InvalidOperationException>(
                () => transaction.AddSavePreparation(() => throw new InvalidOperationException("projection failed")));
        }

        var (verifier, verifyScope) = _fixture.CreateScopeWithRepository();
        using (verifyScope) {
            var persisted = await verifier.FirstOrDefaultAsync(q => q.Where(s => s.Name == "prep-fail"));
            Assert.NotNull(persisted);
            Assert.Equal(1, persisted.Age);
        }
    }

    [Trait("Layer", "Integration")]
    [Fact]
    public async Task SavePreparation_AfterCompleted_ThrowsInvalidOperation() {
        var (_, _, transaction, scope) = _fixture.CreateScopeWithUoW();
        using (scope) {
            await transaction.CommitAsync();
            Assert.Throws<InvalidOperationException>(() => transaction.AddSavePreparation(() => { }));
        }
    }
}
