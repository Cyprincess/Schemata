using System;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.LinqToDB.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Entity.LinqToDB.Integration.Tests;

/// <summary>
///     Behavioral coverage for unique-violation classification at the actual SQL mutation
///     boundaries, per issue #42: a matching-version optimistic update that collides on a
///     unique value becomes ALREADY_EXISTS, a stale version stays ABORTED, and a bulk insert
///     conflict never guesses a row identity.
/// </summary>
[Trait("Category", "Integration")]
public class UniqueClassificationShould : IAsyncLifetime
{
    private readonly IntegrationFixture _fixture = new();

    public Task InitializeAsync() { return _fixture.InitializeAsync(); }

    public Task DisposeAsync() { return _fixture.DisposeAsync(); }

    private static async Task<Course> AddCourseAsync(IntegrationFixture fixture, Guid uid, string title) {
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<Repository.IRepository<Course>>();
        var course = new Course {
            Uid = uid, Title = title, Name = $"courses/{uid:n}", CanonicalName = $"courses/{uid:n}",
        };
        await repository.AddAsync(course);
        await repository.CommitAsync();
        return course;
    }

    private static async Task<Course> LoadCourseAsync(IntegrationFixture fixture, Guid uid) {
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<Repository.IRepository<Course>>();
        var loaded = await repository.FirstOrDefaultAsync(q => q.Where(c => c.Uid == uid));
        Assert.NotNull(loaded);
        return loaded;
    }

    [Fact]
    public async Task Matching_Version_Optimistic_Update_Colliding_On_Unique_Value_ThrowsAlreadyExists() {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await AddCourseAsync(_fixture, a, "alpha");
        await AddCourseAsync(_fixture, b, "beta");

        var entity = await LoadCourseAsync(_fixture, a);
        entity.Title = "beta"; // matching version, but collides with b's unique title

        await using var scope = _fixture.ServiceProvider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<Repository.IRepository<Course>>();

        var ex = await Assert.ThrowsAsync<AlreadyExistsException>(() => repository.UpdateAsync(entity));
        var cause = Assert.IsType<SqliteException>(ex.InnerException);
        Assert.DoesNotContain(cause.Message, JsonSerializer.Serialize(ex.CreateErrorResponse()));
        var info = Assert.Single(ex.Details ?? [], d => d is Abstractions.Errors.ErrorInfoDetail);
        Assert.Equal(Abstractions.SchemataConstants.ErrorReasons.ResourceAlreadyExists,
            ((Abstractions.Errors.ErrorInfoDetail)info).Reason);
    }

    [Fact]
    public async Task Bulk_Insert_Conflict_ThrowsAlreadyExists_Without_Guessing_An_Identity() {
        var uid = Guid.NewGuid();

        await using var scope = _fixture.ServiceProvider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<Repository.IRepository<Course>>();
        var ex = await Assert.ThrowsAsync<AlreadyExistsException>(() => repository.AddRangeAsync([
            new() { Uid = uid,             Title = "bulk-dup", Name = $"courses/{uid:n}" },
            new() { Uid = Guid.NewGuid(), Title = "bulk-dup", Name = "courses/other" },
        ]));
        Assert.IsType<SqliteException>(ex.InnerException);
        // The batch cannot identify which row collided: the resource type carries no name.
        var info = Assert.Single(ex.Details ?? [], d => d is Abstractions.Errors.ResourceInfoDetail);
        Assert.Null(((Abstractions.Errors.ResourceInfoDetail)info).ResourceName);
    }
}
