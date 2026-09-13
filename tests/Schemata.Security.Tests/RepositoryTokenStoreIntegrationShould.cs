using System;
using System.IO;
using System.Linq;
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

    #region IAsyncLifetime Members

    public Task InitializeAsync() {
        // A fixture-private schema keeps parallel test classes from mutating
        // MappingSchema.Default concurrently, which invalidates linq2db's
        // global entity-descriptor caches mid-flight.
        var schema = new MappingSchema();
        schema.AddMetadataReader(new SystemComponentModelDataAnnotationsSchemaAttributeReader());

        var options = new DataOptions().UseSQLite($"Data Source={_dbPath}").UseMappingSchema(schema);

        var services = new ServiceCollection();
        services.TryAddScoped(_ => new TokenConnection(options));
        services.TryAddSingleton<Func<TokenConnection>>(_ => () => new(options));
        services.AddRepository<SchemataToken, LinqToDbRepository<TokenConnection, SchemataToken>>();

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
                count = await new RepositoryTokenStore(repository, TimeProvider.System)
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
                count = await new RepositoryTokenStore(repository, TimeProvider.System)
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
                count = await new RepositoryTokenStore(repository, TimeProvider.System)
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

                count = await new RepositoryTokenStore(repository, time.Object).PruneAsync();
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

    private (IRepository<SchemataToken> Repository, IServiceScope Scope) CreateScope() {
        var scope      = _root!.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<SchemataToken>>();
        return (repository, scope);
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
        return row!.Status!;
    }

    private sealed class TokenConnection : DataConnection
    {
        public TokenConnection(DataOptions options) : base(options) { }
    }
}
