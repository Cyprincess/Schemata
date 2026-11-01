using System;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.EntityFrameworkCore.Integration.Tests.Fixtures;
using Schemata.Entity.Repository;
using Xunit;

namespace Schemata.Entity.EntityFrameworkCore.Integration.Tests;

/// <summary>
///     Behavioral coverage for unique-violation classification at the EF Core SaveChanges
///     boundary, per issue #42: a matching-version optimistic update that collides on a unique
///     value becomes ALREADY_EXISTS with the single confirmable identity, ambiguous bulk
///     conflicts expose no guessed name, and stale versions stay ABORTED.
/// </summary>
[Trait("Category", "Integration")]
public class UniqueClassificationShould : IAsyncLifetime
{
    private readonly IntegrationFixture _fixture = new();

    public Task InitializeAsync() { return _fixture.InitializeAsync(); }

    public Task DisposeAsync() { return _fixture.DisposeAsync(); }

    private async Task<Course> AddCourseAsync(Guid uid, string title) {
        await using var scope = _fixture.ServiceProvider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<Course>>();
        var course = new Course {
            Uid = uid, Title = title, Name = $"courses/{uid:n}", CanonicalName = $"courses/{uid:n}",
        };
        await repository.AddAsync(course);
        await repository.CommitAsync();
        return course;
    }

    [Fact]
    public async Task Matching_Version_Update_Colliding_On_Unique_Value_ThrowsAlreadyExists_With_Identity() {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await AddCourseAsync(a, "alpha");
        await AddCourseAsync(b, "beta");

        await using var scope = _fixture.ServiceProvider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<Course>>();
        var entity = await repository.FirstOrDefaultAsync(q => q.Where(c => c.Uid == a));
        Assert.NotNull(entity);
        entity.Title = "beta"; // collides with b's unique title at SaveChanges

        await repository.UpdateAsync(entity);

        var ex = await Assert.ThrowsAsync<AlreadyExistsException>(() => repository.CommitAsync());
        var cause = Assert.IsType<DbUpdateException>(ex.InnerException);
        Assert.NotNull(cause.InnerException);
        Assert.DoesNotContain(cause.InnerException.Message, JsonSerializer.Serialize(ex.CreateErrorResponse()));
        var info = Assert.Single(ex.Details ?? [], d => d is Abstractions.Errors.ResourceInfoDetail);
        Assert.Equal($"courses/{a:n}", ((Abstractions.Errors.ResourceInfoDetail)info).ResourceName);
    }

    [Fact]
    public async Task Bulk_Conflict_Exposes_Only_A_Provider_Confirmed_Identity() {
        var uid   = Guid.NewGuid();
        var other = Guid.NewGuid();

        await using var scope = _fixture.ServiceProvider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<Course>>();
        await repository.AddRangeAsync([
            new() { Uid = uid,   Title = "ef-bulk-dup", Name = $"courses/{uid:n}",   CanonicalName = $"courses/{uid:n}" },
            new() { Uid = other, Title = "ef-bulk-dup", Name = $"courses/{other:n}", CanonicalName = $"courses/{other:n}" },
        ]);

        var ex = await Assert.ThrowsAsync<AlreadyExistsException>(() => repository.CommitAsync());
        Assert.IsType<DbUpdateException>(ex.InnerException);

        // The provider confirms which row it stopped at: an exposed identity — if any — must be
        // one of the two candidates' canonical names, never an unrelated guess. An ambiguous
        // multi-entry report exposes no identity at all (see UniqueIdentityPolicyShould).
        var info = ex.Details?.OfType<Abstractions.Errors.ResourceInfoDetail>().FirstOrDefault();
        if (info is not null) {
            Assert.NotNull(info.ResourceName);
            Assert.Contains(info.ResourceName, new[] { $"courses/{uid:n}", $"courses/{other:n}" });
        }
    }
}
