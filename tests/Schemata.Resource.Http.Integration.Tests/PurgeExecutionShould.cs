using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.LinqToDB;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Resource.Foundation;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Resource.Http.Integration.Tests;

[Trait("Layer", "Integration")]
public class PurgeExecutionShould
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 205)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 205)]
    public async Task Force_Physically_Removes_Matching_Trash_And_Preserves_Other_Rows(bool linq, int count) {
        await using var database = new Database(linq);
        await database.SeedAsync(count);
        await ExecuteAsync(database.Services, true, CancellationToken.None);
        Assert.Equal(["folders/other/items/deleted", "folders/owner/items/live"], await database.ReadAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Preview_Reports_Eligible_Rows_Without_Mutating_Them(bool linq) {
        await using var database = new Database(linq);
        await database.SeedAsync(205);
        var before = await database.ReadAsync();
        var response = await ExecuteAsync(database.Services, false, CancellationToken.None);
        Assert.Equal(205, response.PurgeCount);
        Assert.All(response.PurgeSample, name => Assert.StartsWith("folders/owner/items/deleted-", name));
        Assert.Equal(before, await database.ReadAsync());
    }

    [Theory]
    [InlineData(false, 1, false)]
    [InlineData(true, 1, false)]
    [InlineData(false, 103, false)]
    [InlineData(true, 103, false)]
    [InlineData(false, 1, true)]
    [InlineData(true, 1, true)]
    [InlineData(false, 103, true)]
    [InlineData(true, 103, true)]
    public async Task Roll_Back_All_Physical_Removals_After_Failure_Or_Cancellation(bool linq, int stop, bool cancel) {
        await using var database = new Database(linq);
        await database.SeedAsync(205);
        var before = await database.ReadAsync();
        using var cancellation = new CancellationTokenSource();
        database.Fault.Stop = stop;
        database.Fault.Cancellation = cancel ? cancellation : null;
        if (cancel) {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ExecuteAsync(database.Services, true, cancellation.Token));
        } else {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => ExecuteAsync(database.Services, true, cancellation.Token));
            Assert.Equal("purge mutation fault", error.Message);
        }
        Assert.Equal(before, await database.ReadAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Advance_Past_Advisor_Blocked_Rows_Without_Bypassing_Their_Policy(bool linq) {
        await using var database = new Database(linq);
        await database.SeedAsync(205);
        database.Fault.Block = true;
        var before = await database.ReadAsync();
        await ExecuteAsync(database.Services, true, CancellationToken.None);
        Assert.Equal(before, await database.ReadAsync());
    }

    private static async Task<PurgeResponse> ExecuteAsync(IServiceProvider services, bool force, CancellationToken ct) {
        using var scope = services.CreateScope();
        var job = ActivatorUtilities.CreateInstance<PurgeJob<Item>>(scope.ServiceProvider);
        var execution = new SchemataJobExecution();
        await job.ExecuteAsync(new JobContext {
            ArgsJson = JsonSerializer.Serialize(new PurgeOperationArgs { Filter = "*", Parent = "folders/owner", Force = force }, SchemataJson.Default),
            Execution = execution,
        }, ct);
        return JsonSerializer.Deserialize<PurgeResponse>(execution.Output!, SchemataJson.Default)!;
    }

    private sealed class Fault : IRepositoryRemoveAdvisor<Item>
    {
        private int _calls;
        public int Order => 0;
        public int Stop { get; set; }
        public bool Block { get; set; }
        public CancellationTokenSource? Cancellation { get; set; }
        public Task<AdviseResult> AdviseAsync(AdviceContext context, IRepository<Item> repository, Item row, CancellationToken ct) {
            if (++_calls == Stop) {
                if (Cancellation is null) throw new InvalidOperationException("purge mutation fault");
                Cancellation.Cancel();
                ct.ThrowIfCancellationRequested();
            }
            return Task.FromResult(Block ? AdviseResult.Block : AdviseResult.Continue);
        }
    }

    private sealed class Database : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public ServiceProvider Services { get; }
        public Fault Fault { get; } = new();
        public Database(bool linq) {
            var connection = $"Data Source=purge-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            _connection = new(connection);
            _connection.Open();
            var services = new ServiceCollection();
            if (linq) {
                var mapping = new MappingSchema();
                mapping.AddMetadataReader(new SystemComponentModelDataAnnotationsSchemaAttributeReader());
                var options = new DataOptions().UseSQLite(connection).UseMappingSchema(mapping);
                services.AddSingleton<Func<PurgeConnection>>(_ => () => new(options));
                services.AddRepository<Item, LinqToDbRepository<PurgeConnection, Item>>();
                using var db = new PurgeConnection(options);
                db.CreateTable<Item>();
            } else {
                services.AddDbContextFactory<PurgeContext>(options => options.UseSqlite(connection).ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
                services.AddRepository<Item, EfCoreRepository<PurgeContext, Item>>();
            }
            services.AddSingleton<IRepositoryRemoveAdvisor<Item>>(Fault);
            Services = services.BuildServiceProvider();
            if (!linq) {
                using var db = Services.GetRequiredService<IDbContextFactory<PurgeContext>>().CreateDbContext();
                db.Database.EnsureCreated();
            }
        }

        public async Task SeedAsync(int count) {
            using var scope = Services.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<Item>>();
            using (repository.SuppressSoftDelete()) {
                for (var i = 0; i < count; i++) {
                    await repository.AddAsync(new() { Folder = "owner", Name = $"deleted-{i:D4}", DeleteTime = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc) });
                }
                await repository.AddAsync(new() { Folder = "other", Name = "deleted", DeleteTime = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc) });
                await repository.AddAsync(new() { Folder = "owner", Name = "live" });
                await repository.CommitAsync();
            }
        }

        public async Task<string[]> ReadAsync() {
            using var scope = Services.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<Item>>();
            using var suppression = repository.SuppressQuerySoftDelete();
            return (await repository.ListAsync(q => q.Select(row => row.CanonicalName!)).ToListAsync()).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        }

        public async ValueTask DisposeAsync() {
            await Services.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    [System.ComponentModel.DataAnnotations.Schema.Table("PurgeItems")]
    [CanonicalName("folders/{folder}/items/{item}")]
    [Schemata.Abstractions.Entities.PrimaryKey(nameof(Uid))]
    public sealed class Item : IIdentifier, ICanonicalName, ISoftDelete
    {
        public Guid Uid { get; set; }
        public string? Folder { get; set; }
        public string? Name { get; set; }
        public string? CanonicalName { get; set; }
        public DateTime? DeleteTime { get; set; }
        public DateTime? PurgeTime { get; set; }
    }

    public sealed class PurgeContext(DbContextOptions<PurgeContext> options) : DbContext(options)
    {
        public DbSet<Item> Items { get; set; } = null!;
    }

    private sealed class PurgeConnection(DataOptions options) : DataConnection(options);
}
