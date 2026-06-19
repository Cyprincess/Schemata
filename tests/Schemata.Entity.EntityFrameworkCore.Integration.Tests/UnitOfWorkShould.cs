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
    public async Task CommitAsync_Twice_ThrowsAfterCompleted() {
        var (repo, scope) = _fixture.CreateScopeWithRepository();
        using (scope) {
            await repo.AddAsync(new() {
                                    FullName = "Double-Commit",
                                    Age      = 18,
                                    Grade    = 1,
                                    Name     = "double-commit",
                                });
            await repo.CommitAsync();

            // The standalone commit completed the implicit unit of work; committing again rejects it.
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await repo.CommitAsync());
        }
    }

    [Fact]
    public async Task WriteAfterCommit_ThrowsAndDoesNotPersist() {
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

                // The unit of work has completed; a further write must fail fast before any
                // autocommit path can persist it.
                await Assert.ThrowsAsync<InvalidOperationException>(async () => await repo.AddAsync(new() {
                    FullName = "After-Commit",
                    Age      = 1,
                    Grade    = 1,
                    Name     = "after-commit-canary",
                }));
            }
        }

        {
            var (verifier, verifyScope) = _fixture.CreateScopeWithRepository();
            using (verifyScope) {
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
}
