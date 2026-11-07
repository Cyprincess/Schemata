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
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.LinqToDB;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Security.Foundation.Stores;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Schemata.Security.Tests.Fixtures;
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
        SecurityTestResources.AddResourceTypeResolver(services);
        // Production resolves the store through DI, so abort classification opens a fresh scope
        // instead of reusing a repository whose unit of work was just disposed.
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<RepositoryTokenStore<SchemataToken>>();

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
                                new() { Name = "target-1", Authorization = "authorizations/auth-1", Status = Statuses.Valid },
                                new() { Name = "target-2", Authorization = "authorizations/auth-1", Status = Statuses.Redeemed },
                                new() { Name = "already",  Authorization = "authorizations/auth-1", Status = Statuses.Revoked },
                                new() { Name = "away",     Authorization = "authorizations/auth-2", Status = Statuses.Valid });
            }
        }

        long count;
        {
            var (repository, scope) = CreateScope();
            using (scope) {
                count = await new RepositoryTokenStore<SchemataToken>(scope.ServiceProvider, TimeProvider.System)
                    .RevokeByAuthorizationAsync("authorizations/auth-1");
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
                count = await new RepositoryTokenStore<SchemataToken>(scope.ServiceProvider, TimeProvider.System)
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
                count = await new RepositoryTokenStore<SchemataToken>(scope.ServiceProvider, TimeProvider.System)
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

                count = await new RepositoryTokenStore<SchemataToken>(scope.ServiceProvider, time.Object).PruneAsync();
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
                        Assert.True(await new RepositoryTokenStore<SchemataToken>(winnerScope.ServiceProvider, TimeProvider.System)
                                         .TryRedeemAsync(current!));
                    }
                }

                Assert.False(await new RepositoryTokenStore<SchemataToken>(loserScope.ServiceProvider, TimeProvider.System).TryRedeemAsync(stale));
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
            SecurityTestResources.AddResourceTypeResolver(services);
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddScoped<RepositoryTokenStore<SchemataToken>>();
            await using var provider = services.BuildServiceProvider();

            var scope = provider.CreateScope();
            using (scope) {
                var store = scope.ServiceProvider.GetRequiredService<RepositoryTokenStore<SchemataToken>>();
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

                var replaced = await new RepositoryTokenStore<SchemataToken>(scope.ServiceProvider, time.Object)
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
                var reused = await new RepositoryTokenStore<SchemataToken>(scope.ServiceProvider, time.Object)
                    .GetOrCreateAsync("users/u-1", "op-session", "sid-1", "gen-other", TimeSpan.FromMinutes(5));

                Assert.Equal("gen-new", reused.Value);
                Assert.Equal(Now.AddMinutes(5), reused.ExpireTime);
                Assert.Equal("gen-new", (await Find(repository, "slot"))!.Value);
            }
        }
    }


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Predecessor_Write_Veto_Fails_The_Rotation_And_Leaves_No_Partial_Successor(bool linq) {
        var veto = new UpdateVeto(row => row.Name == "rat");
        await using var host = await VetoHost.CreateAsync(linq,
            services => services.TryAddEnumerable(ServiceDescriptor.Singleton<IRepositoryUpdateAdvisor<SchemataToken>>(veto)));

        {
            var (repository, scope) = host.CreateRepositoryScope();
            using (scope) {
                await repository.AddAsync(new SchemataToken {
                    Name = "rat", ReferenceId = "rat-1", Application = "applications/client-1",
                    Type = "registration", Status = Statuses.Valid,
                });
                await repository.CommitAsync();
            }
        }

        {
            var (store, scope) = host.CreateStoreScope();
            using (scope) {
                var repository  = scope.ServiceProvider.GetRequiredService<IRepository<SchemataToken>>();
                var predecessor = await Find(repository, "rat");
                Assert.NotNull(predecessor);

                await Assert.ThrowsAsync<FailedPreconditionException>(() => store.TryRotateAsync(predecessor!, [
                    new() {
                        Name = "rat-next", ReferenceId = "rat-2", Application = "applications/client-1",
                        Type = "registration", Status = Statuses.Valid,
                    },
                ]));
            }
        }

        {
            var (repository, scope) = host.CreateRepositoryScope();
            using (scope) {
                Assert.Equal(Statuses.Valid, await StatusOf(repository, "rat"));
                Assert.Null(await Find(repository, "rat-next"));
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Second_Successor_Write_Veto_Fails_The_Rotation_And_Rolls_Back_The_First(bool linq) {
        var veto = new AddVeto(row => row.Name == "rat-second");
        await using var host = await VetoHost.CreateAsync(linq,
            services => services.TryAddEnumerable(ServiceDescriptor.Singleton<IRepositoryAddAdvisor<SchemataToken>>(veto)));

        {
            var (repository, scope) = host.CreateRepositoryScope();
            using (scope) {
                await repository.AddAsync(new SchemataToken {
                    Name = "rat", ReferenceId = "rat-1", Application = "applications/client-1",
                    Type = "registration", Status = Statuses.Valid,
                });
                await repository.CommitAsync();
            }
        }

        {
            var (store, scope) = host.CreateStoreScope();
            using (scope) {
                var repository  = scope.ServiceProvider.GetRequiredService<IRepository<SchemataToken>>();
                var predecessor = await Find(repository, "rat");
                Assert.NotNull(predecessor);

                await Assert.ThrowsAsync<FailedPreconditionException>(() => store.TryRotateAsync(predecessor!, [
                    new() {
                        Name = "rat-first", ReferenceId = "rat-2", Application = "applications/client-1",
                        Type = "registration", Status = Statuses.Valid,
                    },
                    new() {
                        Name = "rat-second", ReferenceId = "rat-3", Application = "applications/client-1",
                        Type = "registration", Status = Statuses.Valid,
                    },
                ]));
            }
        }

        {
            var (repository, scope) = host.CreateRepositoryScope();
            using (scope) {
                Assert.Equal(Statuses.Valid, await StatusOf(repository, "rat"));
                Assert.Null(await Find(repository, "rat-first"));
                Assert.Null(await Find(repository, "rat-second"));
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Marker_Write_Veto_Fails_The_Family_Publication_Without_Partial_Members(bool linq) {
        var veto = new UpdateVeto(row => row.Type == TokenTypes.Family);
        await using var host = await VetoHost.CreateAsync(linq,
            services => services.TryAddEnumerable(ServiceDescriptor.Singleton<IRepositoryUpdateAdvisor<SchemataToken>>(veto)));

        {
            var (store, scope) = host.CreateStoreScope();
            using (scope) {
                // The marker is created through an add, which the veto does not block.
                Assert.True(await store.PublishFamilyAsync("family-1", [
                    new() { Name = "member-1", Family = "family-1", Type = "access_token", Status = Statuses.Valid },
                ], create: true));
            }
        }

        {
            var (store, scope) = host.CreateStoreScope();
            using (scope) {
                await Assert.ThrowsAsync<FailedPreconditionException>(() => store.PublishFamilyAsync("family-1", [
                    new() { Name = "member-2", Family = "family-1", Type = "access_token", Status = Statuses.Valid },
                ], create: false));
            }
        }

        {
            var (repository, scope) = host.CreateRepositoryScope();
            using (scope) {
                Assert.Equal(Statuses.FamilyActive, await FamilyStatusOf(repository, "family-1"));
                Assert.Equal(Statuses.Valid, await StatusOf(repository, "member-1"));
                Assert.Null(await Find(repository, "member-2"));
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_Add_Veto_Fails_And_Persists_No_Row(bool linq) {
        var veto = new AddVeto(row => row.Name == "created");
        await using var host = await VetoHost.CreateAsync(linq,
            services => services.TryAddEnumerable(ServiceDescriptor.Singleton<IRepositoryAddAdvisor<SchemataToken>>(veto)));

        {
            var (store, scope) = host.CreateStoreScope();
            using (scope) {
                await Assert.ThrowsAsync<FailedPreconditionException>(
                    () => store.CreateAsync(new SchemataToken { Name = "created", ReferenceId = "created" }));
            }
        }

        {
            var (repository, scope) = host.CreateRepositoryScope();
            using (scope) {
                Assert.Null(await Find(repository, "created"));
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Update_Update_Veto_Fails_And_Persists_The_Stored_Row(bool linq) {
        var veto = new UpdateVeto(row => row.Name == "slot");
        await using var host = await VetoHost.CreateAsync(linq,
            services => services.TryAddEnumerable(ServiceDescriptor.Singleton<IRepositoryUpdateAdvisor<SchemataToken>>(veto)));

        {
            var (repository, scope) = host.CreateRepositoryScope();
            using (scope) {
                await repository.AddAsync(new SchemataToken { Name = "slot", Value = "old" });
                await repository.CommitAsync();
            }
        }

        {
            var (store, scope) = host.CreateStoreScope();
            using (scope) {
                var repository = scope.ServiceProvider.GetRequiredService<IRepository<SchemataToken>>();
                var token      = await Find(repository, "slot");
                Assert.NotNull(token);
                token!.Value = "new";

                await Assert.ThrowsAsync<FailedPreconditionException>(() => store.UpdateAsync(token));
            }
        }

        {
            var (repository, scope) = host.CreateRepositoryScope();
            using (scope) {
                var stored = await Find(repository, "slot");
                Assert.NotNull(stored);
                Assert.Equal("old", stored!.Value);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_Commit_Failure_Persists_No_Row(bool linq) {
        await using var host = await VetoHost.CreateAsync(linq, _ => { });
        var failure = new IOException("create commit failed");

        {
            var (store, scope) = host.CreateStoreScope();
            using (scope) {
                var caught = await Assert.ThrowsAsync<IOException>(() => store.CreateAsync(
                    new SchemataToken { Name = "failed" }, beforeCommit: (_, _) => Task.FromException(failure)));
                Assert.Same(failure, caught);
            }
        }

        {
            var (repository, scope) = host.CreateRepositoryScope();
            using (scope) {
                Assert.Null(await Find(repository, "failed"));
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetOrCreate_Lost_Insert_With_No_Winner_Fails_Closed(bool linq) {
        var loser = new LosingAdd(row => row.Key == "slot-1");
        await using var host = await VetoHost.CreateAsync(linq,
            services => services.TryAddEnumerable(ServiceDescriptor.Singleton<IRepositoryAddAdvisor<SchemataToken>>(loser)));

        {
            var (store, scope) = host.CreateStoreScope();
            using (scope) {
                await Assert.ThrowsAsync<AbortedException>(
                    () => store.GetOrCreateAsync("users/u-1", "dpop", "slot-1", "value", TimeSpan.FromMinutes(5)));
            }
        }

        {
            var (repository, scope) = host.CreateRepositoryScope();
            using (scope) {
                Assert.Null(await repository.SingleOrDefaultAsync(
                    q => q.Where(t => t.Parent == "users/u-1" && t.Provider == "dpop" && t.Key == "slot-1")));
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rotation_Commit_Failure_Rolls_Back_The_Predecessor_And_Successors(bool linq) {
        await using var host = await VetoHost.CreateAsync(linq, _ => { });

        {
            var (repository, scope) = host.CreateRepositoryScope();
            using (scope) {
                await repository.AddAsync(new SchemataToken {
                    Name = "rat", ReferenceId = "rat-1", Application = "applications/client-1",
                    Type = "registration", Status = Statuses.Valid,
                });
                await repository.CommitAsync();
            }
        }

        var failure = new IOException("rotation commit failed");
        {
            var (store, scope) = host.CreateStoreScope();
            using (scope) {
                var repository  = scope.ServiceProvider.GetRequiredService<IRepository<SchemataToken>>();
                var predecessor = await Find(repository, "rat");
                Assert.NotNull(predecessor);

                var caught = await Assert.ThrowsAsync<IOException>(() => store.TryRotateAsync(predecessor!, [
                    new() {
                        Name = "rat-next", ReferenceId = "rat-2", Application = "applications/client-1",
                        Type = "registration", Status = Statuses.Valid,
                    },
                ], beforeCommit: (_, _) => Task.FromException(failure)));
                Assert.Same(failure, caught);
            }
        }

        {
            var (repository, scope) = host.CreateRepositoryScope();
            using (scope) {
                Assert.Equal(Statuses.Valid, await StatusOf(repository, "rat"));
                Assert.Null(await Find(repository, "rat-next"));
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentSlotCreation_LeavesOneDurableRow(bool linq) {
        await using var host = await VetoHost.CreateAsync(linq, _ => { });

        var attempts = await Task.WhenAll(
            CaptureSlotAsync(host, "first"),
            CaptureSlotAsync(host, "second"));

        Assert.All(attempts, AssertAllowedConflict);
        var (repository, scope) = host.CreateRepositoryScope();
        using (scope) {
            Assert.Equal(1, await repository.CountAsync(query => query.Where(row =>
                row.Parent == "applications/client" && row.Provider == "nonce" && row.Key == "slot")));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentExpiredSlotReplacement_LeavesOneDurableRow(bool linq) {
        await using var host = await VetoHost.CreateAsync(linq, _ => { });
        var (seed, seedScope) = host.CreateRepositoryScope();
        using (seedScope) {
            await SeedAsync(seed, new SchemataToken {
                Name = "slot",
                Parent = "applications/client",
                Provider = "nonce",
                Key = "slot",
                Value = "expired",
                ExpireTime = DateTime.UtcNow - TimeSpan.FromMinutes(1),
            });
        }

        var attempts = await Task.WhenAll(
            CaptureSlotAsync(host, "first"),
            CaptureSlotAsync(host, "second"));

        Assert.All(attempts, AssertAllowedConflict);
        var (repository, scope) = host.CreateRepositoryScope();
        using (scope) {
            var row = Assert.Single(await repository.ListAsync(query => query.Where(row =>
                row.Parent == "applications/client" && row.Provider == "nonce" && row.Key == "slot")).ToListAsync());
            Assert.NotEqual("expired", row.Value);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentFamilyPublication_LeavesOneFamilyMarker(bool linq) {
        await using var host = await VetoHost.CreateAsync(linq, _ => { });

        var attempts = await Task.WhenAll(
            CaptureFamilyAsync(host, "first"),
            CaptureFamilyAsync(host, "second"));

        Assert.All(attempts, AssertAllowedConflict);
        var (repository, scope) = host.CreateRepositoryScope();
        using (scope) {
            Assert.Equal(1, await repository.CountAsync(query => query.Where(row => row.FamilyKey == "family")));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentRotation_LeavesOneReferenceId(bool linq) {
        await using var host = await VetoHost.CreateAsync(linq, _ => { });
        SchemataToken predecessor;
        var (seed, seedScope) = host.CreateRepositoryScope();
        using (seedScope) {
            predecessor = new() {
                Name = "predecessor",
                ReferenceId = "predecessor",
                Status = Statuses.Valid,
                Timestamp = Guid.NewGuid(),
            };
            await SeedAsync(seed, predecessor);
        }

        var attempts = await Task.WhenAll(
            CaptureRotationAsync(host, "first"),
            CaptureRotationAsync(host, "second"));

        Assert.All(attempts, AssertAllowedConflict);
        var (repository, scope) = host.CreateRepositoryScope();
        using (scope) {
            Assert.Equal(1, await repository.CountAsync(query => query.Where(row => row.ReferenceId == "successor")));
        }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task Logical_Key_Write_Races_Leave_One_Durable_Match(bool linq, bool familyKey, bool update) {
        await using var host = await VetoHost.CreateAsync(linq, _ => { });
        if (update) {
            var (seed, seedScope) = host.CreateRepositoryScope();
            using (seedScope) {
                await SeedAsync(seed,
                    new SchemataToken {
                        Name = "first", FamilyKey = "first-family", ReferenceId = "first-reference", Status = Statuses.Valid,
                    },
                    new SchemataToken {
                        Name = "second", FamilyKey = "second-family", ReferenceId = "second-reference", Status = Statuses.Valid,
                    });
            }
        }

        var attempts = await Task.WhenAll(
            CaptureLogicalWriteAsync(host, "first", familyKey, update),
            CaptureLogicalWriteAsync(host, "second", familyKey, update));

        Assert.Contains(attempts, outcome => outcome is WriteOutcome.Applied or WriteOutcome.Transient);
        var (repository, scope) = host.CreateRepositoryScope();
        using (scope) {
            Assert.Equal(1, await CountLogicalKeyAsync(repository, familyKey, "shared-key"));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Update_Retains_Own_Logical_Keys(bool linq) {
        await using var host = await VetoHost.CreateAsync(linq, _ => { });
        var (seed, seedScope) = host.CreateRepositoryScope();
        using (seedScope) {
            await SeedAsync(seed, new SchemataToken {
                Name = "current", FamilyKey = "current-family", ReferenceId = "current-reference", Status = Statuses.Valid,
            });
        }

        var (store, scope) = host.CreateStoreScope();
        using (scope) {
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<SchemataToken>>();
            var current = await Find(repository, "current");
            Assert.NotNull(current);
            current.Value = "updated";
            await store.UpdateAsync(current);

            var stored = await Find(repository, "current");
            Assert.NotNull(stored);
            Assert.Equal("updated", stored.Value);
            Assert.Equal("current-family", stored.FamilyKey);
            Assert.Equal("current-reference", stored.ReferenceId);
        }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task Multi_Token_Writes_Reject_Duplicate_Staged_Logical_Keys(
        bool linq,
        bool familyPublication,
        bool familyKey) {
        await using var host = await VetoHost.CreateAsync(linq, _ => { });
        if (!familyPublication) {
            var (seed, seedScope) = host.CreateRepositoryScope();
            using (seedScope) {
                await SeedAsync(seed, new SchemataToken {
                    Name = "predecessor", ReferenceId = "predecessor-reference", Status = Statuses.Valid,
                });
            }
        }

        var (store, scope) = host.CreateStoreScope();
        using (scope) {
            SchemataToken Member(string name, string reference) {
                return new() {
                    Name = name,
                    ReferenceId = familyKey ? reference : "shared-key",
                    FamilyKey = familyKey ? "shared-key" : null,
                    Status = Statuses.Valid,
                };
            }

            var members = new[] { Member("first-member", "first-reference"), Member("second-member", "second-reference") };
            if (familyPublication) {
                await Assert.ThrowsAsync<AlreadyExistsException>(
                    () => store.PublishFamilyAsync("published-family", members, create: true));
            } else {
                var repository = scope.ServiceProvider.GetRequiredService<IRepository<SchemataToken>>();
                var predecessor = await Find(repository, "predecessor");
                Assert.NotNull(predecessor);
                await Assert.ThrowsAsync<AlreadyExistsException>(() => store.TryRotateAsync(predecessor, members));
            }
        }

        var (verify, verifyScope) = host.CreateRepositoryScope();
        using (verifyScope) {
            Assert.Equal(0, await CountLogicalKeyAsync(verify, familyKey, "shared-key"));
            if (familyPublication) {
                Assert.Equal(0, await verify.CountAsync(query => query.Where(row => row.FamilyKey == "published-family")));
            } else {
                Assert.Equal(Statuses.Valid, await StatusOf(verify, "predecessor"));
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Family_Publication_Lock_Failure_Remains_Unclassified_Aborted(bool linq) {
        await using var host = await VetoHost.CreateAsync(linq, _ => { });
        await using var blocker = new SqliteConnection($"Data Source={host.DatabasePath};Pooling=False");
        await blocker.OpenAsync();
        await using (var begin = blocker.CreateCommand()) {
            begin.CommandText = "BEGIN IMMEDIATE";
            await begin.ExecuteNonQueryAsync();
        }

        try {
            var (store, scope) = host.CreateStoreScope();
            using (scope) {
                var error = await Assert.ThrowsAsync<AbortedException>(() => store.PublishFamilyAsync("locked-family", [
                    new SchemataToken { Name = "locked-member", ReferenceId = "locked-reference", Status = Statuses.Valid },
                ], create: true));
                Assert.NotNull(error.InnerException);
                Assert.DoesNotContain(error.Details ?? [], detail => detail is Schemata.Abstractions.Errors.ResourceInfoDetail);
                Assert.False(SchemataTokenConflicts.IsConflict<SchemataToken>(error));
            }
        } finally {
            await using var rollback = blocker.CreateCommand();
            rollback.CommandText = "ROLLBACK";
            await rollback.ExecuteNonQueryAsync();
        }

        var (repository, repositoryScope) = host.CreateRepositoryScope();
        using (repositoryScope) {
            Assert.Equal(0, await repository.CountAsync(query => query.Where(row => row.FamilyKey == "locked-family")));
            Assert.Null(await Find(repository, "locked-member"));
        }
    }

    private enum WriteOutcome
    {
        Applied,
        AlreadyExists,
        Cas,
        Transient,
    }

    private static async Task<WriteOutcome> CaptureLogicalWriteAsync(
        VetoHost host,
        string name,
        bool familyKey,
        bool update) {
        var (store, scope) = host.CreateStoreScope();
        using (scope) {
            try {
                if (update) {
                    var repository = scope.ServiceProvider.GetRequiredService<IRepository<SchemataToken>>();
                    var token = await Find(repository, name);
                    Assert.NotNull(token);
                    SetLogicalKey(token, familyKey, "shared-key");
                    await store.UpdateAsync(token);
                } else {
                    var token = new SchemataToken { Name = name, Status = Statuses.Valid };
                    SetLogicalKey(token, familyKey, "shared-key");
                    await store.CreateAsync(token);
                }

                return WriteOutcome.Applied;
            } catch (AlreadyExistsException) {
                return WriteOutcome.AlreadyExists;
            } catch (AbortedException error) when (SchemataTokenConflicts.IsConflict<SchemataToken>(error)) {
                return WriteOutcome.Cas;
            } catch (AbortedException) {
                return WriteOutcome.Transient;
            }
        }
    }

    private static void SetLogicalKey(SchemataToken token, bool familyKey, string value) {
        if (familyKey) {
            token.FamilyKey = value;
        } else {
            token.ReferenceId = value;
        }
    }

    private static ValueTask<int> CountLogicalKeyAsync(
        IRepository<SchemataToken> repository,
        bool familyKey,
        string value) {
        return repository.CountAsync(query => familyKey
            ? query.Where(row => row.FamilyKey == value)
            : query.Where(row => row.ReferenceId == value));
    }

    private static async Task<Exception?> CaptureSlotAsync(VetoHost host, string value) {
        var (store, scope) = host.CreateStoreScope();
        using (scope) {
            try {
                await store.GetOrCreateAsync("applications/client", "nonce", "slot", value, TimeSpan.FromMinutes(5));
                return null;
            } catch (Exception exception) {
                return exception;
            }
        }
    }

    private static async Task<Exception?> CaptureFamilyAsync(VetoHost host, string reference) {
        var (store, scope) = host.CreateStoreScope();
        using (scope) {
            try {
                await store.PublishFamilyAsync("family", [
                    new() { Name = reference, ReferenceId = reference, Family = "family", Status = Statuses.Valid },
                ], create: true);
                return null;
            } catch (Exception exception) {
                return exception;
            }
        }
    }

    private static async Task<Exception?> CaptureRotationAsync(VetoHost host, string name) {
        var (store, scope) = host.CreateStoreScope();
        using (scope) {
            try {
                var current = await store.FindByReferenceIdAsync("predecessor");
                Assert.NotNull(current);
                await store.TryRotateAsync(current, [
                    new() { Name = name, ReferenceId = "successor", Status = Statuses.Valid },
                ]);
                return null;
            } catch (Exception exception) {
                return exception;
            }
        }
    }

    private static void AssertAllowedConflict(Exception? error) {
        Assert.True(error is null or AlreadyExistsException or AbortedException, error?.ToString());
    }

    private (IRepository<SchemataToken> Repository, IServiceScope Scope) CreateScope() {
        var scope      = _root!.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<SchemataToken>>();
        return (repository, scope);
    }

    private (RepositoryTokenStore<SchemataToken> Store, IServiceScope Scope) CreateStoreScope() {
        var scope = _root!.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<RepositoryTokenStore<SchemataToken>>();
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

    private static async Task<string> FamilyStatusOf(IRepository<SchemataToken> repository, string family) {
        var marker = await repository.SingleOrDefaultAsync(
            q => q.Where(t => t.FamilyKey == family && t.Type == TokenTypes.Family));
        Assert.NotNull(marker);
        return marker.Status!;
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
            [EnumeratorCancellation] CancellationToken ct = default,
            QueryOptions options = default) {
            await foreach (var row in base.ListAsync(predicate, ct, options)) {
                yield return row;
            }
            await _race();
        }
    }

    private sealed class TokenNameAdvisor : IRepositoryAddAdvisor<SchemataToken>
    {
        public int Order => AdviceAddCanonicalName.DefaultOrder - 1;

        public Task<AdviseResult> AdviseAsync(
            AdviceContext context,
            IRepository<SchemataToken> repository,
            SchemataToken entity,
            MutationOptions options,
            CancellationToken ct) {
            entity.Name ??= Guid.NewGuid().ToString("n");
            return Task.FromResult(AdviseResult.Continue);
        }
    }

    // Isolated dual-provider host for advisor-veto and commit-failure scenarios; each instance owns
    // a private SQLite file so the registered veto state cannot leak into the shared class fixture.
    private sealed class VetoHost(ServiceProvider services, string path) : IAsyncDisposable
    {
        public ServiceProvider Services => services;

        public string DatabasePath => path;

        public static async Task<VetoHost> CreateAsync(bool linq, Action<IServiceCollection> configure) {
            var path = Path.Combine(Path.GetTempPath(), $"schemata-token-veto-{Guid.NewGuid():N}.db");
            var services = new ServiceCollection();
            if (linq) {
                var schema = new MappingSchema();
                schema.AddMetadataReader(new SystemComponentModelDataAnnotationsSchemaAttributeReader());
                var options = new DataOptions().UseSQLite($"Data Source={path};Pooling=False").UseMappingSchema(schema);
                services.AddSingleton<Func<TokenConnection>>(_ => () => new(options));
                using var db = new TokenConnection(options);
                db.CreateTable<SchemataToken>();
                services.AddRepository<SchemataToken, LinqToDbRepository<TokenConnection, SchemataToken>>();
            } else {
                services.AddDbContextFactory<VetoContext>(o => o.UseSqlite($"Data Source={path};Pooling=False")
                    .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
                services.AddRepository<SchemataToken, EfCoreRepository<VetoContext, SchemataToken>>();
            }
            SecurityTestResources.AddResourceTypeResolver(services);
            services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataToken>, TokenNameAdvisor>());
            services.AddSingleton(TimeProvider.System);
            services.AddScoped<RepositoryTokenStore<SchemataToken>>();
            configure(services);
            var root = services.BuildServiceProvider();
            if (!linq) {
                await using var db = await root.GetRequiredService<IDbContextFactory<VetoContext>>().CreateDbContextAsync();
                await db.Database.EnsureCreatedAsync();
            }
            return new(root, path);
        }

        public (IRepository<SchemataToken> Repository, IServiceScope Scope) CreateRepositoryScope() {
            var scope = Services.CreateScope();
            return (scope.ServiceProvider.GetRequiredService<IRepository<SchemataToken>>(), scope);
        }

        public (RepositoryTokenStore<SchemataToken> Store, IServiceScope Scope) CreateStoreScope() {
            var scope = Services.CreateScope();
            return (scope.ServiceProvider.GetRequiredService<RepositoryTokenStore<SchemataToken>>(), scope);
        }

        public async ValueTask DisposeAsync() {
            await services.DisposeAsync();
            File.Delete(path);
        }
    }

    private sealed class VetoContext(DbContextOptions<VetoContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model) {
            model.Entity<SchemataToken>();
        }
    }

    private sealed class AddVeto(Func<SchemataToken, bool> matches) : IRepositoryAddAdvisor<SchemataToken>
    {
        public int Order => 0;

        public Task<AdviseResult> AdviseAsync(
            AdviceContext              context,
            IRepository<SchemataToken> repository,
            SchemataToken              entity,
            MutationOptions      options,
            CancellationToken          ct
        ) {
            return Task.FromResult(matches(entity) ? AdviseResult.Block : AdviseResult.Continue);
        }
    }

    // The unique index reporting the loss of a slot insert without a persisted winner.
    private sealed class LosingAdd(Func<SchemataToken, bool> matches) : IRepositoryAddAdvisor<SchemataToken>
    {
        public int Order => 0;

        public Task<AdviseResult> AdviseAsync(
            AdviceContext              context,
            IRepository<SchemataToken> repository,
            SchemataToken              entity,
            MutationOptions      options,
            CancellationToken          ct
        ) {
            return matches(entity)
                ? Task.FromException<AdviseResult>(new AlreadyExistsException())
                : Task.FromResult(AdviseResult.Continue);
        }
    }

    private sealed class UpdateVeto(Func<SchemataToken, bool> matches) : IRepositoryUpdateAdvisor<SchemataToken>
    {
        public int Order => 0;

        public Task<AdviseResult> AdviseAsync(
            AdviceContext              context,
            IRepository<SchemataToken> repository,
            SchemataToken              entity,
            MutationOptions      options,
            CancellationToken          ct
        ) {
            return Task.FromResult(matches(entity) ? AdviseResult.Block : AdviseResult.Continue);
        }
    }
}
