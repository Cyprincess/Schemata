using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Schemata.Entity.LinqToDB;
using Schemata.Entity.Repository;
using Schemata.Security.Foundation.Stores;
using Schemata.Security.Skeleton.Entities;
using Xunit;
using static Schemata.Security.Skeleton.SecurityConstants;

namespace Schemata.Security.Tests;

[Trait("Category", "Integration")]
public class RepositoryTokenStoreIntegrationShould : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly string _dbPath = $"{Guid.NewGuid():n}.db";

    private ServiceProvider? _root;

    private DataOptions? _options;

    #region IAsyncLifetime Members

    public Task InitializeAsync() {
        // A fixture-private schema keeps parallel test classes from mutating
        // MappingSchema.Default concurrently, which invalidates linq2db's
        // global entity-descriptor caches mid-flight.
        var schema = new MappingSchema();
        schema.AddMetadataReader(new SystemComponentModelDataAnnotationsSchemaAttributeReader());

        var options = new DataOptions().UseSQLite($"Data Source={_dbPath}").UseMappingSchema(schema);
        _options = options;

        var services = new ServiceCollection();
        services.TryAddScoped(_ => new TokenConnection(options));
        services.TryAddSingleton<Func<TokenConnection>>(_ => () => new(options));
        services.AddRepository<SchemataToken, LinqToDbRepository<TokenConnection, SchemataToken>>();
        // Production resolves the store through DI, so abort classification opens a fresh scope
        // instead of reusing a repository whose unit of work was just disposed.
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<RepositoryTokenStore>();

        _root = services.BuildServiceProvider();

        using var scope      = _root.CreateScope();
        var       connection = scope.ServiceProvider.GetRequiredService<TokenConnection>();
        connection.CreateTable<SchemataToken>(tableOptions: TableOptions.CreateIfNotExists);

        return Task.CompletedTask;
    }

    public Task DisposeAsync() {
        _root?.Dispose();

        SqliteConnection.ClearAllPools();

        if (File.Exists(_dbPath)) {
            File.Delete(_dbPath);
        }

        return Task.CompletedTask;
    }

    #endregion

    [Fact]
    public async Task Revoke_By_Authorization_Persists_Revocation_Of_Only_Matching_NonRevoked_Rows() {
        {
            var (repository, scope) = CreateScope();
            using (scope) {
                await SeedAsync(repository,
                                new() { Name = "target-1", Authorization = "auth-1", Status = Statuses.Valid },
                                new() { Name = "target-2", Authorization = "auth-1", Status = Statuses.Redeemed },
                                new() { Name = "already",  Authorization = "auth-1", Status = Statuses.Revoked },
                                new() { Name = "away",     Authorization = "auth-2", Status = Statuses.Valid });
            }
        }

        long count;
        {
            var (repository, scope) = CreateScope();
            using (scope) {
                count = await new RepositoryTokenStore(scope.ServiceProvider, TimeProvider.System)
                    .RevokeByAuthorizationAsync("auth-1");
            }
        }

        {
            var (repository, scope) = CreateScope();
            using (scope) {
                Assert.Equal(2, count);
                Assert.Equal(Statuses.Revoked, await StatusOf(repository, "target-1"));
                Assert.Equal(Statuses.Revoked, await StatusOf(repository, "target-2"));
                Assert.Equal(Statuses.Revoked, await StatusOf(repository, "already"));
                Assert.Equal(Statuses.Valid,   await StatusOf(repository, "away"));
            }
        }
    }

    [Fact]
    public async Task Revoke_By_Session_Persists_Revocation_Of_Only_Matching_NonRevoked_Rows() {
        {
            var (repository, scope) = CreateScope();
            using (scope) {
                await SeedAsync(repository,
                                new() { Name = "live-1", SessionId = "sid-1", Status = Statuses.Valid },
                                new() { Name = "live-2", SessionId = "sid-1", Status = Statuses.Redeemed },
                                new() { Name = "away",   SessionId = "sid-2", Status = Statuses.Valid });
            }
        }

        long count;
        {
            var (repository, scope) = CreateScope();
            using (scope) {
                count = await new RepositoryTokenStore(scope.ServiceProvider, TimeProvider.System)
                    .RevokeBySessionAsync("sid-1");
            }
        }

        {
            var (repository, scope) = CreateScope();
            using (scope) {
                Assert.Equal(2, count);
                Assert.Equal(Statuses.Revoked, await StatusOf(repository, "live-1"));
                Assert.Equal(Statuses.Revoked, await StatusOf(repository, "live-2"));
                Assert.Equal(Statuses.Valid,   await StatusOf(repository, "away"));
            }
        }
    }

    [Fact]
    public async Task Revoke_By_Device_Persists_Revocation_Of_Only_Matching_NonRevoked_Rows() {
        {
            var (repository, scope) = CreateScope();
            using (scope) {
                await SeedAsync(repository,
                                new() { Name = "live-1", DeviceId = "device-1", Status = Statuses.Valid },
                                new() { Name = "live-2", DeviceId = "device-1", Status = Statuses.Redeemed },
                                new() { Name = "away",   DeviceId = "device-2", Status = Statuses.Valid });
            }
        }

        long count;
        {
            var (repository, scope) = CreateScope();
            using (scope) {
                count = await new RepositoryTokenStore(scope.ServiceProvider, TimeProvider.System)
                    .RevokeByDeviceAsync("device-1");
            }
        }

        {
            var (repository, scope) = CreateScope();
            using (scope) {
                Assert.Equal(2, count);
                Assert.Equal(Statuses.Revoked, await StatusOf(repository, "live-1"));
                Assert.Equal(Statuses.Revoked, await StatusOf(repository, "live-2"));
                Assert.Equal(Statuses.Valid,   await StatusOf(repository, "away"));
            }
        }
    }

    [Fact]
    public async Task Prune_Persists_Removal_Of_Expired_And_Revoked_Rows() {
        {
            var (repository, scope) = CreateScope();
            using (scope) {
                await SeedAsync(repository,
                                new() { Name = "expired", Status = Statuses.Valid, ExpireTime = Now.AddSeconds(-1) },
                                new() { Name = "revoked", Status = Statuses.Revoked },
                                new() { Name = "live",    Status = Statuses.Valid, ExpireTime = Now.AddMinutes(5) });
            }
        }

        long count;
        {
            var (repository, scope) = CreateScope();
            using (scope) {
                var time = new Mock<TimeProvider>();
                time.Setup(t => t.GetUtcNow()).Returns(new DateTimeOffset(Now, TimeSpan.Zero));

                count = await new RepositoryTokenStore(scope.ServiceProvider, time.Object).PruneAsync();
            }
        }

        {
            var (repository, scope) = CreateScope();
            using (scope) {
                Assert.Equal(2, count);
                Assert.Null(await Find(repository, "expired"));
                Assert.Null(await Find(repository, "revoked"));
                Assert.Equal(Statuses.Valid, await StatusOf(repository, "live"));
            }
        }
    }

    [Fact]
    public async Task TryRedeem_Loses_The_Race_And_Returns_False_When_Another_Scope_Redeems_First() {
        {
            var (repository, scope) = CreateScope();
            using (scope) {
                await SeedAsync(repository,
                                new SchemataToken { Name = "code", ReferenceId = "ref-code", Status = Statuses.Valid });
            }
        }

        // The loser loads the row before the winner redeems it, so its CAS register is stale.
        {
            var (loser, loserScope) = CreateScope();
            using (loserScope) {
                var stale = await Find(loser, "code");
                Assert.NotNull(stale);

                {
                    var (winner, winnerScope) = CreateScope();
                    using (winnerScope) {
                        var current = await Find(winner, "code");
                        Assert.True(await new RepositoryTokenStore(winnerScope.ServiceProvider, TimeProvider.System)
                                         .TryRedeemAsync(current!));
                    }
                }

                Assert.False(await new RepositoryTokenStore(loserScope.ServiceProvider, TimeProvider.System).TryRedeemAsync(stale));
            }
        }

        {
            var (repository, scope) = CreateScope();
            using (scope) {
                Assert.Equal(Statuses.Redeemed, await StatusOf(repository, "code"));
            }
        }
    }

    [Fact]
    public async Task Rotate_Publishes_The_Successor_And_Redeems_The_Predecessor() {
        {
            var (repository, scope) = CreateScope();
            using (scope) {
                await SeedAsync(repository,
                                new SchemataToken {
                                    Name = "rat", ReferenceId = "rat-1", Application = "applications/client-1",
                                    Type = "registration", Status = Statuses.Valid,
                                });
            }
        }

        bool rotated;
        {
            var (store, scope) = CreateStoreScope();
            using (scope) {
                var repository  = scope.ServiceProvider.GetRequiredService<IRepository<SchemataToken>>();
                var predecessor = await Find(repository, "rat");
                Assert.NotNull(predecessor);

                rotated = await store.TryRotateAsync(predecessor, [
                    new() {
                        Name = "rat-next", ReferenceId = "rat-2", Application = "applications/client-1",
                        Type = "registration", Status = Statuses.Valid,
                    },
                ]);
            }
        }

        {
            var (repository, scope) = CreateScope();
            using (scope) {
                Assert.True(rotated);
                Assert.Equal(Statuses.Redeemed, await StatusOf(repository, "rat"));
                Assert.Equal(Statuses.Valid, await StatusOf(repository, "rat-next"));
            }
        }
    }

    [Fact]
    public async Task Concurrent_Rotations_Leave_One_Winner_And_No_Orphan_Successor() {
        {
            var (repository, scope) = CreateScope();
            using (scope) {
                await SeedAsync(repository,
                                new SchemataToken {
                                    Name = "rat", ReferenceId = "rat-1", Application = "applications/client-1",
                                    Type = "registration", Status = Statuses.Valid,
                                });
            }
        }

        // The loser loads the row before the winner rotates it, so its CAS register is stale.
        {
            var (loserStore, loserScope) = CreateStoreScope();
            using (loserScope) {
                var loser = loserScope.ServiceProvider.GetRequiredService<IRepository<SchemataToken>>();
                var stale = await Find(loser, "rat");
                Assert.NotNull(stale);

                {
                    var (winnerStore, winnerScope) = CreateStoreScope();
                    using (winnerScope) {
                        var winner  = winnerScope.ServiceProvider.GetRequiredService<IRepository<SchemataToken>>();
                        var current = await Find(winner, "rat");
                        Assert.True(await winnerStore.TryRotateAsync(current!, [
                            new() {
                                Name = "rat-winner", ReferenceId = "rat-2",
                                Application = "applications/client-1",
                                Type = "registration", Status = Statuses.Valid,
                            },
                        ]));
                    }
                }

                Assert.False(await loserStore.TryRotateAsync(stale, [
                    new() {
                        Name = "rat-loser", ReferenceId = "rat-3",
                        Application = "applications/client-1",
                        Type = "registration", Status = Statuses.Valid,
                    },
                ]));
            }
        }

        {
            var (repository, scope) = CreateScope();
            using (scope) {
                Assert.Equal(Statuses.Redeemed, await StatusOf(repository, "rat"));
                Assert.Equal(Statuses.Valid, await StatusOf(repository, "rat-winner"));
                Assert.Null(await Find(repository, "rat-loser"));
            }
        }
    }

    [Fact]
    public async Task Rotate_After_Application_Revocation_Fails_Closed() {
        {
            var (repository, scope) = CreateScope();
            using (scope) {
                await SeedAsync(repository,
                                new SchemataToken {
                                    Name = "rat", ReferenceId = "rat-1", Application = "applications/client-1",
                                    Type = "registration", Status = Statuses.Valid,
                                },
                                new SchemataToken {
                                    Name = "away", ReferenceId = "away-1", Application = "applications/client-2",
                                    Type = "registration", Status = Statuses.Valid,
                                },
                                new SchemataToken {
                                    Name = "fact", Application = "applications/client-1",
                                    Type = TokenTypes.SessionParticipant, Status = Statuses.Valid,
                                    Parent = "users/u-1", Provider = TokenTypes.SessionParticipant,
                                    Key = "sid-1\u001eapplications/client-1",
                                });
            }
        }

        {
            var (staleStore, staleScope) = CreateStoreScope();
            using (staleScope) {
                var stale = staleScope.ServiceProvider.GetRequiredService<IRepository<SchemataToken>>();
                var predecessor = await Find(stale, "rat");
                Assert.NotNull(predecessor);

                {
                    var (revokerStore, revokerScope) = CreateStoreScope();
                    using (revokerScope) {
                        Assert.Equal(1, await revokerStore.RevokeByApplicationAsync("applications/client-1"));
                    }
                }

                Assert.False(await staleStore.TryRotateAsync(predecessor, [
                    new() {
                        Name = "rat-next", ReferenceId = "rat-2",
                        Application = "applications/client-1",
                        Type = "registration", Status = Statuses.Valid,
                    },
                ]));
            }
        }

        {
            var (repository, scope) = CreateScope();
            using (scope) {
                Assert.Equal(Statuses.Revoked, await StatusOf(repository, "rat"));
                Assert.Null(await Find(repository, "rat-next"));
                Assert.Equal(Statuses.Valid, await StatusOf(repository, "away"));
                Assert.Equal(Statuses.Valid, await StatusOf(repository, "fact"));
            }
        }
    }

    [Fact]
    public async Task Revoke_By_Application_Retries_And_Drains_The_Rotation_Winner_And_Unchanged_Rows() {
        {
            var (repository, scope) = CreateScope();
            using (scope) {
                await SeedAsync(repository,
                                new SchemataToken {
                                    Name = "access", ReferenceId = "at-1", Application = "applications/client-1",
                                    Type = "access_token", Status = Statuses.Valid,
                                },
                                new SchemataToken {
                                    Name = "rat", ReferenceId = "rat-1", Application = "applications/client-1",
                                    Type = "registration", Status = Statuses.Valid,
                                });
            }
        }

        // A committed winner rotation lands after the attempt's list but before its first update,
        // so the rotated row's loaded Timestamp is stale and the optimistic update aborts.
        var raced = false;
        async Task WinnerRotates() {
            if (raced) {
                return;
            }

            raced = true;

            var (store, scope) = CreateStoreScope();
            using (scope) {
                var repository = scope.ServiceProvider.GetRequiredService<IRepository<SchemataToken>>();
                var current    = await Find(repository, "rat");
                Assert.True(await store.TryRotateAsync(current!, [
                    new() {
                        Name = "rat-winner", ReferenceId = "rat-2", Application = "applications/client-1",
                        Type = "registration", Status = Statuses.Valid,
                    },
                ]));
            }
        }

        long count;
        {
            var services = new ServiceCollection();
            services.TryAddScoped(_ => new TokenConnection(_options!));
            services.TryAddSingleton<Func<TokenConnection>>(_ => () => new(_options!));
            services.TryAddSingleton(_ => (Func<Task>)WinnerRotates);
            services.AddRepository<SchemataToken, RaceAfterReadRepository>();
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddScoped<RepositoryTokenStore>();
            await using var provider = services.BuildServiceProvider();

            var scope = provider.CreateScope();
            using (scope) {
                var store = scope.ServiceProvider.GetRequiredService<RepositoryTokenStore>();
                count = await store.RevokeByApplicationAsync("applications/client-1");
            }
        }

        {
            var (repository, scope) = CreateScope();
            using (scope) {
                Assert.Equal(3, count);
                Assert.Equal(Statuses.Revoked, await StatusOf(repository, "access"));
                Assert.Equal(Statuses.Revoked, await StatusOf(repository, "rat"));
                Assert.Equal(Statuses.Revoked, await StatusOf(repository, "rat-winner"));
            }
        }
    }

    [Fact]
    public async Task GetOrCreate_Replaces_An_Expired_Slot_And_Observes_One_Winner() {
        {
            var (repository, scope) = CreateScope();
            using (scope) {
                await SeedAsync(repository,
                                new SchemataToken {
                                    Name = "slot", Parent = "users/u-1", Provider = "op-session", Key = "sid-1",
                                    Value = "gen-old", ExpireTime = Now.AddMinutes(-5),
                                });
            }
        }

        {
            var (repository, scope) = CreateScope();
            using (scope) {
                var time = new Mock<TimeProvider>();
                time.Setup(t => t.GetUtcNow()).Returns(new DateTimeOffset(Now, TimeSpan.Zero));

                var replaced = await new RepositoryTokenStore(scope.ServiceProvider, time.Object)
                    .GetOrCreateAsync("users/u-1", "op-session", "sid-1", "gen-new", TimeSpan.FromMinutes(5));

                Assert.Equal("gen-new", replaced.Value);
                Assert.Equal(Now.AddMinutes(5), replaced.ExpireTime);
            }
        }

        {
            var (repository, scope) = CreateScope();
            using (scope) {
                var time = new Mock<TimeProvider>();
                time.Setup(t => t.GetUtcNow()).Returns(new DateTimeOffset(Now, TimeSpan.Zero));

                // A later establishment while the slot is live reuses the winner's value and
                // leaves its remaining lifetime untouched.
                var reused = await new RepositoryTokenStore(scope.ServiceProvider, time.Object)
                    .GetOrCreateAsync("users/u-1", "op-session", "sid-1", "gen-other", TimeSpan.FromMinutes(5));

                Assert.Equal("gen-new", reused.Value);
                Assert.Equal(Now.AddMinutes(5), reused.ExpireTime);
                Assert.Equal("gen-new", (await Find(repository, "slot"))!.Value);
            }
        }
    }


    private (IRepository<SchemataToken> Repository, IServiceScope Scope) CreateScope() {
        var scope      = _root!.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<SchemataToken>>();
        return (repository, scope);
    }

    private (RepositoryTokenStore Store, IServiceScope Scope) CreateStoreScope() {
        var scope = _root!.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<RepositoryTokenStore>();
        return (store, scope);
    }

    private static async Task SeedAsync(IRepository<SchemataToken> repository, params SchemataToken[] rows) {
        foreach (var row in rows) {
            await repository.AddAsync(row);
        }

        await repository.CommitAsync();
    }

    private static async Task<SchemataToken?> Find(IRepository<SchemataToken> repository, string name) {
        return await repository.SingleOrDefaultAsync(q => q.Where(t => t.Name == name));
    }

    private static async Task<string> StatusOf(IRepository<SchemataToken> repository, string name) {
        var row = await Find(repository, name);
        Assert.NotNull(row);
        return row.Status!;
    }

    private sealed class TokenConnection : DataConnection
    {
        public TokenConnection(DataOptions options) : base(options) { }
    }

    // Complete the winner after draining the stale read and before Begin acquires SQLite's write lock.
    private sealed class RaceAfterReadRepository : LinqToDbRepository<TokenConnection, SchemataToken>
    {
        private readonly Func<Task> _race;

        public RaceAfterReadRepository(IServiceProvider sp, Func<TokenConnection> factory, Func<Task> race)
            : base(sp, factory) {
            _race = race;
        }
        public override async IAsyncEnumerable<TResult> ListAsync<TResult>(
            Func<IQueryable<SchemataToken>, IQueryable<TResult>>? predicate,
            [EnumeratorCancellation] CancellationToken ct = default) {
            await foreach (var row in base.ListAsync(predicate, ct)) {
                yield return row;
            }
            await _race();
        }
    }
}
