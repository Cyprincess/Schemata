using System;
using System.Linq;
using System.Threading.Tasks;
using Schemata.Entity.EntityFrameworkCore.Integration.Tests.Fixtures;
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
                using var work = repo.BeginWork();
                await repo.AddAsync(
                    new() {
                        FullName = "UoW-Alice",
                        Age      = 18,
                        Grade    = 1,
                        Name     = "uow-alice",
                    }
                );
                await repo.AddAsync(
                    new() {
                        FullName = "UoW-Bob",
                        Age      = 19,
                        Grade    = 2,
                        Name     = "uow-bob",
                    }
                );
                await work.CommitAsync();
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
                using var work = repo.BeginWork();
                await repo.AddAsync(
                    new() {
                        FullName = "Rollback-Alice",
                        Age      = 18,
                        Grade    = 1,
                        Name     = "rollback-alice",
                    }
                );
                await work.RollbackAsync();
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
                using var work = repo.BeginWork();
                await repo.AddAsync(
                    new() {
                        FullName = "Dispose-Alice",
                        Age      = 18,
                        Grade    = 1,
                        Name     = "dispose-alice",
                    }
                );
                // intentionally no commit/rollback
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
    public async Task CommitAsync_InsideUoW_ThrowsInvalidOperation() {
        var (repo, scope) = _fixture.CreateScopeWithRepository();
        using (scope) {
            using var work = repo.BeginWork();
            await repo.AddAsync(
                new() {
                    FullName = "Throw-Alice",
                    Age      = 18,
                    Grade    = 1,
                    Name     = "throw-alice",
                }
            );

            await Assert.ThrowsAsync<InvalidOperationException>(() => repo.CommitAsync().AsTask());
        }
    }

    [Fact]
    public async Task CrossRepository_SharesTransaction() {
        {
            var (studentRepo, courseRepo, uow, scope) = _fixture.CreateScopeWithUoW();
            using (scope) {
                uow.Begin();
                await studentRepo.AddAsync(
                    new() {
                        FullName = "Cross-Alice",
                        Age      = 18,
                        Grade    = 1,
                        Name     = "cross-alice",
                    }
                );
                await courseRepo.AddAsync(
                    new() {
                        Title = "Cross-Course", Credits = 3, Name = "cross-course",
                    }
                );
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
    public async Task BeginWork_WhenAlreadyActive_ThrowsInvalidOperation() {
        var (repo, scope) = _fixture.CreateScopeWithRepository();
        using (scope) {
            using var work = repo.BeginWork();
            Assert.Throws<InvalidOperationException>(() => repo.BeginWork());
        }
    }
}
