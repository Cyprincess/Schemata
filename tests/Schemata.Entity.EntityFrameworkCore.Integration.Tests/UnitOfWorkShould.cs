using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Entity.EntityFrameworkCore.Integration.Tests.Fixtures;
using Schemata.Entity.Repository;
using Xunit;

namespace Schemata.Entity.EntityFrameworkCore.Integration.Tests;

[Trait("Category", "Integration")]
public class UnitOfWorkShould : IAsyncLifetime
{
    private readonly IntegrationFixture _fixture = new();

    #region IAsyncLifetime Members

    public Task InitializeAsync() { return _fixture.InitializeAsync(); }

    public Task DisposeAsync() { return _fixture.DisposeAsync(); }

    #endregion

    [Fact]
    public async Task CommitAsync_CommitsMultipleOperations() {
        {
            var (repo, scope) = _fixture.CreateScopeWithRepository();
            using (scope) {
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
                repo.Join(uow);
                await repo.AddAsync(new() {
                                        FullName = "UoW-Alice",
                                        Age      = 18,
                                        Grade    = 1,
                                        Name     = "uow-alice",
                                    });
                await repo.AddAsync(new() {
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
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
                repo.Join(uow);
                await repo.AddAsync(new() {
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
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
                repo.Join(uow);
                await repo.AddAsync(new() {
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
                                               FullName = "Cross-Alice",
                                               Age      = 18,
                                               Grade    = 1,
                                               Name     = "cross-alice",
                                           });
                await courseRepo.AddAsync(new() {
                                              Title = "Cross-Course", Credits = 3, Name = "cross-course",
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
    public async Task Join_AfterUncommittedWork_ThrowsInvalidOperation() {
        var (repo, _, uow, scope) = _fixture.CreateScopeWithUoW();
        using (scope) {
            await repo.AddAsync(new() {
                                    FullName = "Uncommitted",
                                    Age      = 18,
                                    Grade    = 1,
                                    Name     = "uncommitted-join",
                                });

            Assert.Throws<InvalidOperationException>(() => repo.Join(uow));

            // The first AddAsync enlisted an implicit unit of work; its standalone commit
            // persists the staged work.
            await repo.CommitAsync();
        }
    }

    [Fact]
    public async Task CommitAsync_Twice_IsANoOpOnItsOwnUnitOfWork() {
        var (repo, scope) = _fixture.CreateScopeWithRepository();
        using (scope) {
            await repo.AddAsync(new() {
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
                                        FullName = "Before-Commit",
                                        Age      = 18,
                                        Grade    = 1,
                                        Name     = "before-commit",
                                    });
                await repo.CommitAsync();

                // The repository reopened its own unit of work, so the second write stages into a new
                // one and stays uncommitted until its own CommitAsync.
                await repo.AddAsync(new() {
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
    public async Task ReopenItsOwnUnitOfWork_AfterCommit() {
        var (repo, scope) = _fixture.CreateScopeWithRepository();
        using (scope) {
            await repo.AddAsync(new() { FullName = "Reopen-A", Age = 20, Grade = 1, Name = "reopen-a" });
            await repo.CommitAsync();

            await repo.AddAsync(new() { FullName = "Reopen-B", Age = 21, Grade = 2, Name = "reopen-b" });
            await repo.CommitAsync();
        }

        var (verify, verifyScope) = _fixture.CreateScopeWithRepository();
        using (verifyScope) {
            Assert.Equal(2, await verify.CountAsync(q => q.Where(s => s.Name!.StartsWith("reopen-"))));
        }
    }

    [Fact]
    public async Task KeepReadsWorking_BetweenACommitAndTheNextWrite() {
        var (repo, scope) = _fixture.CreateScopeWithRepository();
        using (scope) {
            await repo.AddAsync(new() { FullName = "Between-A", Age = 20, Grade = 1, Name = "between-a" });
            await repo.CommitAsync();

            Assert.Equal(1, await repo.CountAsync(q => q.Where(s => s.Name == "between-a")));

            await repo.AddAsync(new() { FullName = "Between-B", Age = 21, Grade = 2, Name = "between-b" });
            await repo.CommitAsync();

            Assert.Equal(2, await repo.CountAsync(q => q.Where(s => s.Name!.StartsWith("between-"))));
        }
    }

    [Fact]
    public async Task NotReopenACallerSuppliedUnitOfWork_AfterItCommits() {
        var (repo, scope) = _fixture.CreateScopeWithRepository();
        using (scope) {
            await using var uow = repo.Begin();
            await repo.AddAsync(new() { FullName = "Joined-A", Age = 20, Grade = 1, Name = "joined-a" });
            await uow.CommitAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await repo.AddAsync(new() { FullName = "Joined-B", Age = 21, Grade = 2, Name = "joined-b" }));
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
}
