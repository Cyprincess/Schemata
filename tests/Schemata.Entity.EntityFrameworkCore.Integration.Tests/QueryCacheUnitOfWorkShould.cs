using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Entity.EntityFrameworkCore.Integration.Tests.Fixtures;
using Schemata.Entity.Repository;
using Xunit;

namespace Schemata.Entity.EntityFrameworkCore.Integration.Tests;

[Trait("Category", "Integration")]
public class QueryCacheUnitOfWorkShould : IAsyncLifetime
{
    private readonly IntegrationFixture _fixture = new(useQueryCache: true);

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Trait("Layer", "Integration")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Joined_ReadOnly_Repository_Bypasses_Warm_Reads_And_Cold_Fills(bool commit) {
        await SeedAsync();
        Assert.Equal("Original", await ReadNameAsync());

        using var scope = _fixture.ServiceProvider.CreateScope();
        using var writer = scope.ServiceProvider.GetRequiredService<IRepository<Student>>();
        using var reader = scope.ServiceProvider.GetRequiredService<IRepository<Student>>();
        using var uow = writer.Begin();
        reader.Join(uow);
        var context = ((IUnitOfWork<TestDbContext>)uow).Context;
        await using var sqlTransaction = await context.Database.BeginTransactionAsync();
        var student = (await writer.FirstOrDefaultAsync<Student>(null))!;
        student.FullName = "Transaction";
        student.Age = 20;
        Assert.Equal(MutationResult.Applied, await writer.UpdateAsync(student));

        // Scalar SQL reads preserve EF's buffered staging boundary.
        Assert.Equal("Original", await reader.FirstOrDefaultAsync(q => q.Select(s => s.FullName)));
        // The consumer explicitly flushes this shared context inside its SQL transaction.
        await context.SaveChangesAsync();
        Assert.Equal("Transaction", await reader.FirstOrDefaultAsync(q => q.Select(s => s.FullName)));
        Assert.Equal(20, await reader.FirstOrDefaultAsync(q => q.Select(s => s.Age)));

        string? callbackValue = null;
        uow.AddCommitSink(CommitOrders.Resource, async ct => callbackValue = await ReadNameAsync(ct));
        if (commit) {
            await sqlTransaction.CommitAsync();
            await uow.CommitAsync();
            Assert.Equal("Transaction", callbackValue);
        } else {
            await sqlTransaction.RollbackAsync();
            await uow.RollbackAsync();
            Assert.Null(callbackValue);
            var factory = _fixture.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();
            await using var independent = await factory.CreateDbContextAsync();
            await independent.Students.ExecuteUpdateAsync(set => set.SetProperty(s => s.FullName, "Uncached SQL"));
        }

        Assert.Equal(commit ? "Transaction" : "Original", await ReadNameAsync());
        using var freshScope = _fixture.ServiceProvider.CreateScope();
        using var fresh = freshScope.ServiceProvider.GetRequiredService<IRepository<Student>>();
        Assert.Equal(commit ? 20 : 10, await fresh.FirstOrDefaultAsync(q => q.Select(s => s.Age)));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await reader.FirstOrDefaultAsync(q => q.Select(s => s.FullName)));
    }

    [Trait("Layer", "Integration")]
    [Fact]
    public async Task Implicit_Staging_Does_Not_Flush_Queries_Or_Publish_Uncommitted_Results() {
        using var scope = _fixture.ServiceProvider.CreateScope();
        using var repository = scope.ServiceProvider.GetRequiredService<IRepository<Student>>();
        await repository.AddAsync(new() { Uid = Guid.NewGuid(), Name = "pending", FullName = "Pending" });
        Assert.Equal(0, await repository.CountAsync<Student>(null));
        await repository.CommitAsync();
        Assert.Equal(1, await repository.CountAsync<Student>(null));
        Assert.Equal("Pending", await repository.FirstOrDefaultAsync(q => q.Select(s => s.FullName)));
    }

    [Trait("Layer", "Integration")]
    [Fact]
    public async Task Standalone_Commit_Reopens_Cache_Reads_And_Fills() {
        await SeedAsync();
        using var scope = _fixture.ServiceProvider.CreateScope();
        using var repository = scope.ServiceProvider.GetRequiredService<IRepository<Student>>();
        var student = (await repository.FirstOrDefaultAsync<Student>(null))!;
        student.FullName = "Committed";
        await repository.UpdateAsync(student);
        await repository.CommitAsync();
        Assert.Equal("Committed", await repository.FirstOrDefaultAsync(q => q.Select(s => s.FullName)));

        var factory = _fixture.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();
        await using (var context = await factory.CreateDbContextAsync()) {
            await context.Students.ExecuteUpdateAsync(set => set.SetProperty(s => s.FullName, "Uncached SQL"));
        }
        Assert.Equal("Committed", await ReadNameAsync());
    }

    [Trait("Layer", "Integration")]
    [Fact]
    public async Task Joined_Query_After_Disposal_Cannot_Serve_Shared_Cache() {
        await SeedAsync();
        Assert.Equal("Original", await ReadNameAsync());
        using var scope = _fixture.ServiceProvider.CreateScope();
        using var reader = scope.ServiceProvider.GetRequiredService<IRepository<Student>>();
        var uow = reader.Begin();
        await uow.DisposeAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await reader.FirstOrDefaultAsync(q => q.Select(s => s.FullName)));
        Assert.Equal("Original", await ReadNameAsync());
    }

    [Trait("Layer", "Integration")]
    [Fact]
    public async Task Disposed_Standalone_Repository_Cannot_Return_A_Warm_Result() {
        await SeedAsync();
        Assert.Equal("Original", await ReadNameAsync());
        using var scope = _fixture.ServiceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<Student>>();
        repository.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await repository.FirstOrDefaultAsync(q => q.Select(s => s.FullName)));
    }

    [Trait("Layer", "Integration")]
    [Fact]
    public async Task Joined_CrossEntity_Projection_Reads_Flushed_Transaction_And_Rollback_Restores_Rows() {
        await SeedAsync();
        using var scope = _fixture.ServiceProvider.CreateScope();
        using var courses = scope.ServiceProvider.GetRequiredService<IRepository<Course>>();
        await courses.AddAsync(new() { Uid = Guid.NewGuid(), Name = "course", Title = "Original course", Credits = 3 });
        await courses.CommitAsync();
        var factory = _fixture.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();
        using (var preload = new AssociationRepository(scope.ServiceProvider, factory)) {
            Assert.Equal("Original course", await preload.ReadCourseTitleAsync());
        }

        using var students = new AssociationRepository(scope.ServiceProvider, factory);
        using var uow = courses.Begin();
        students.Join(uow);
        var shared = ((IUnitOfWork<TestDbContext>)uow).Context;
        await using var sqlTransaction = await shared.Database.BeginTransactionAsync();
        var course = (await courses.FirstOrDefaultAsync<Course>(null))!;
        course.Title = "Transaction course";
        await courses.UpdateAsync(course);
        await shared.SaveChangesAsync();
        Assert.Equal("Transaction course", await students.ReadCourseTitleAsync());
        await sqlTransaction.RollbackAsync();
        await uow.RollbackAsync();

        using var independent = _fixture.ServiceProvider.CreateScope();
        using var freshCourses = independent.ServiceProvider.GetRequiredService<IRepository<Course>>();
        Assert.Equal("Original course", (await freshCourses.FirstOrDefaultAsync<Course>(null))?.Title);
    }

    private async Task SeedAsync() {
        using var scope = _fixture.ServiceProvider.CreateScope();
        using var repository = scope.ServiceProvider.GetRequiredService<IRepository<Student>>();
        await repository.AddAsync(new() { Uid = Guid.NewGuid(), Name = "student", FullName = "Original", Age = 10 });
        await repository.CommitAsync();
    }

    private async Task<string?> ReadNameAsync(CancellationToken ct = default) {
        using var scope = _fixture.ServiceProvider.CreateScope();
        using var repository = scope.ServiceProvider.GetRequiredService<IRepository<Student>>();
        return await repository.FirstOrDefaultAsync(q => q.Select(s => s.FullName), ct);
    }

    private sealed class AssociationRepository(IServiceProvider services, IDbContextFactory<TestDbContext> factory)
        : EfCoreRepository<TestDbContext, Student>(services, factory)
    {
        public ValueTask<string?> ReadCourseTitleAsync() {
            var courses = Context.Courses;
            return FirstOrDefaultAsync(q => q.Select(student => courses.Select(course => course.Title).FirstOrDefault()));
        }
    }
}
