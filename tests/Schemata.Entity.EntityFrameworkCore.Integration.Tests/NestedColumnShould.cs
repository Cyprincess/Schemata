using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Entity.Repository;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.EntityFrameworkCore.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Entity.EntityFrameworkCore.Integration.Tests;

/// <summary>
///     Behavioral coverage for the declared nested-value column contract on the EF Core
///     bridge, per issue #137: an explicitly declared <c>[Column]</c> member outside the
///     automatic JSON eligibility set maps through the JSON converter, while automatic-set
///     controls keep their representation.
/// </summary>
[Trait("Category", "Integration")]
public class NestedColumnShould : IAsyncLifetime
{
    private readonly IntegrationFixture _fixture = new();

    #region IAsyncLifetime Members

    public Task InitializeAsync() { return _fixture.InitializeAsync(); }

    public Task DisposeAsync() { return _fixture.DisposeAsync(); }

    #endregion

    [Fact]
    public async Task DeclaredNestedDictionary_RoundTripsLosslessly_AcrossFreshScopes() {
        var uid = Guid.NewGuid();
        var entity = new NestedThing {
            Uid       = uid,
            Name      = $"nestedThings/{uid:n}",
            Map       = new() {
                ["alpha"] = ["one", "two"],
                ["beta"]  = [],
            },
            SimpleMap = new() { ["key"] = "value" },
            Tags      = ["a", "b"],
        };

        {
            var (repository, scope) = CreateScope();
            using (scope) {
                await repository.AddAsync(entity);
                await repository.CommitAsync();
            }
        }

        {
            var (repository, scope) = CreateScope();
            using (scope) {
                var loaded = await repository.FirstOrDefaultAsync(q => q.Where(e => e.Uid == uid));
                Assert.NotNull(loaded);
                Assert.NotNull(loaded.Map);
                Assert.Equal(2, loaded.Map.Count);
                Assert.Equal(new() { "one", "two" }, loaded.Map["alpha"]);
                Assert.Empty(loaded.Map["beta"]);
                Assert.Equal("value", loaded.SimpleMap?["key"]);
                Assert.Equal(new() { "a", "b" }, loaded.Tags);
            }
        }
    }

    [Fact]
    public async Task ContentEqualNestedValue_StaysUnchanged_AcrossLoadAndReplacement() {
        var uid = Guid.NewGuid();
        await SeedAsync(uid);

        var (context, scope) = CreateContextScope();
        using (scope) {
            var loaded = await context.NestedThings.SingleAsync(e => e.Uid == uid);

            Assert.Equal(EntityState.Unchanged, ChangeState(context, loaded));

            loaded.Map = new() {
                ["alpha"] = ["one", "two"],
                ["beta"]  = [],
            };

            Assert.Equal(EntityState.Unchanged, ChangeState(context, loaded));
        }
    }

    [Fact]
    public async Task NestedValueComparer_HashesContentEqualSnapshotsConsistently() {
        var uid = Guid.NewGuid();
        await SeedAsync(uid);

        var (context, scope) = CreateContextScope();
        using (scope) {
            var loaded   = await context.NestedThings.SingleAsync(e => e.Uid == uid);
            var comparer = context.Model
                                  .FindEntityType(typeof(NestedThing))!
                                  .FindProperty(nameof(NestedThing.Map))!
                                  .GetValueComparer();

            var snapshot = comparer.Snapshot(loaded.Map);

            Assert.True(comparer.Equals(loaded.Map, snapshot));
            Assert.Equal(comparer.GetHashCode(loaded.Map), comparer.GetHashCode(snapshot));

            var mutated = (Dictionary<string, List<string>>)snapshot!;
            mutated["alpha"] = ["one", "two", "three"];

            Assert.False(comparer.Equals(loaded.Map, mutated));
        }
    }

    [Fact]
    public async Task InPlaceNestedMutation_DetectedAsModified_AndPersistsAcrossScopes() {
        var uid = Guid.NewGuid();
        await SeedAsync(uid);

        {
            var (context, scope) = CreateContextScope();
            using (scope) {
                var loaded = await context.NestedThings.SingleAsync(e => e.Uid == uid);
                loaded.Map!["alpha"].Add("three");

                Assert.Equal(EntityState.Modified, ChangeState(context, loaded));

                await context.SaveChangesAsync();
            }
        }

        {
            var (context, scope) = CreateContextScope();
            using (scope) {
                var reloaded = await context.NestedThings.AsNoTracking().SingleAsync(e => e.Uid == uid);
                Assert.Equal(new() { "one", "two", "three" }, reloaded.Map!["alpha"]);
            }
        }
    }

    private async Task SeedAsync(Guid uid) {
        var (repository, scope) = CreateScope();
        using (scope) {
            await repository.AddAsync(new NestedThing {
                Uid  = uid,
                Name = $"nestedThings/{uid:n}",
                Map  = new() {
                    ["alpha"] = ["one", "two"],
                    ["beta"]  = [],
                },
            });
            await repository.CommitAsync();
        }
    }

    private (TestDbContext Context, IServiceScope Scope) CreateContextScope() {
        var scope = _fixture.ServiceProvider.CreateScope();
        return (scope.ServiceProvider.GetRequiredService<TestDbContext>(), scope);
    }

    private static EntityState ChangeState(TestDbContext context, NestedThing entity) {
        return context.ChangeTracker.Entries<NestedThing>().Single(e => ReferenceEquals(e.Entity, entity)).State;
    }

    [Fact]
    public async Task ReadOnlyStampedNestedEntity_KeepsTimestamp_WhenSiblingWriteCommits() {
        var uid       = Guid.NewGuid();
        var siblingId = Guid.NewGuid();
        await SeedStampedAsync(uid);
        await SeedStampedAsync(siblingId);

        Guid stamp;
        Guid siblingStamp;
        {
            var (repository, scope) = CreateStampedScope();
            using (scope) {
                // The repository adopts the unit of work's context at the first staged write,
                // so the read-only entity must load after UpdateAsync: only then is it tracked
                // by the context SaveChanges runs on, and change detection consults the
                // nested-value comparer on it.
                var sibling = await repository.FirstOrDefaultAsync(q => q.Where(e => e.Uid == siblingId));
                Assert.NotNull(sibling);
                siblingStamp = sibling.Timestamp;
                sibling.Map!["alpha"].Add("three");
                await repository.UpdateAsync(sibling);

                var loaded = await repository.FirstOrDefaultAsync(q => q.Where(e => e.Uid == uid));
                Assert.NotNull(loaded);
                stamp = loaded.Timestamp;

                await repository.CommitAsync();

                Assert.Equal(stamp, loaded.Timestamp);
                Assert.NotEqual(siblingStamp, sibling.Timestamp);
            }
        }

        {
            var (repository, scope) = CreateStampedScope();
            using (scope) {
                var reloaded = await repository.FirstOrDefaultAsync(q => q.Where(e => e.Uid == uid));
                Assert.NotNull(reloaded);
                Assert.Equal(stamp, reloaded.Timestamp);
            }
        }
    }

    [Fact]
    public async Task StaleStampedNestedWrite_Aborts() {
        var uid = Guid.NewGuid();
        await SeedStampedAsync(uid);

        Guid stale;
        {
            var (repository, scope) = CreateStampedScope();
            using (scope) {
                var loaded = await repository.FirstOrDefaultAsync(q => q.Where(e => e.Uid == uid));
                Assert.NotNull(loaded);
                stale = loaded.Timestamp;
            }
        }

        // A fresh write with a nested edit commits and rotates the stamp.
        {
            var (repository, scope) = CreateStampedScope();
            using (scope) {
                var loaded = await repository.FirstOrDefaultAsync(q => q.Where(e => e.Uid == uid));
                Assert.NotNull(loaded);
                loaded.Map!["alpha"].Add("three");
                await repository.UpdateAsync(loaded);
                await repository.CommitAsync();
                Assert.NotEqual(stale, loaded.Timestamp);
            }
        }

        {
            var (repository, scope) = CreateStampedScope();
            using (scope) {
                var loaded = await repository.FirstOrDefaultAsync(q => q.Where(e => e.Uid == uid));
                Assert.NotNull(loaded);
                loaded.Timestamp = stale;
                loaded.Map!["alpha"].Add("four");
                await repository.UpdateAsync(loaded);
                await Assert.ThrowsAsync<AbortedException>(() => repository.CommitAsync());
            }
        }
    }

    private async Task SeedStampedAsync(Guid uid) {
        var (repository, scope) = CreateStampedScope();
        using (scope) {
            await repository.AddAsync(new StampedNestedThing {
                Uid  = uid,
                Name = $"stampedNestedThings/{uid:n}",
                Map  = new() {
                    ["alpha"] = ["one", "two"],
                    ["beta"]  = [],
                },
            });
            await repository.CommitAsync();
        }
    }

    private (IRepository<StampedNestedThing> Repository, IServiceScope Scope) CreateStampedScope() {
        var scope      = _fixture.ServiceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<StampedNestedThing>>();
        return (repository, scope);
    }

    private (IRepository<NestedThing> Repository, IServiceScope Scope) CreateScope() {
        var scope      = _fixture.ServiceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<NestedThing>>();
        return (repository, scope);
    }
}
