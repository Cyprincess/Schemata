using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository.Advisors;
using Schemata.Entity.Repository.Integration.Tests.Fixtures;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Tests;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Entity.Repository.Integration.Tests;

[Trait("Category", "Integration")]
[Trait("Layer", "Integration")]
public class ResourceMutationShould
{
    [Fact]
    public async Task Create_OwnsTransaction_CommitsAndReturnsApplied() {
        await using var host = await CreateHostAsync();

        MutationResult result;
        string         canonicalName;
        using (var scope = host.Root.CreateScope()) {
            var mutation = scope.ServiceProvider.GetRequiredService<IResourceMutation<SchemataProcess>>();
            var entity   = NewProcess();

            result        = await mutation.CreateAsync(entity);
            canonicalName = entity.CanonicalName!;
        }

        Assert.Equal(MutationResult.Applied, result);

        var reloaded = await FindProcessAsync(host, canonicalName);
        Assert.NotNull(reloaded);
    }

    [Fact]
    public async Task Create_ConsecutiveMutations_AreIndependentTransactions() {
        await using var host = await CreateHostAsync();

        string firstName;
        using (var scope = host.Root.CreateScope()) {
            // One scoped mutation instance drives two consecutive self-committing operations: each
            // must resolve, stage, commit, and dispose its own repository and unit of work.
            var mutation = scope.ServiceProvider.GetRequiredService<IResourceMutation<SchemataProcess>>();

            var first = NewProcess();
            Assert.Equal(MutationResult.Applied, await mutation.CreateAsync(first));
            firstName = first.CanonicalName!;

            Assert.Equal(MutationResult.Applied, await mutation.CreateAsync(NewProcess()));
        }

        Assert.NotNull(await FindProcessAsync(host, firstName));
        Assert.Equal(2, await CountProcessesAsync(host));
    }

    [Fact]
    public async Task Create_ThenUpdateInOuterTransaction_AppliesFinalInsertAndOrderedCallbacks() {
        await using var host = await CreateHostAsync();

        SchemataProcess entity;
        Guid initialStamp;
        DateTime? createTime;
        using (var scope = host.Root.CreateScope()) {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
            var mutation = scope.ServiceProvider.GetRequiredService<IResourceMutation<SchemataProcess>>();
            entity = NewProcess();

            Assert.Equal(MutationResult.Applied, await mutation.CreateAsync(entity, uow));
            initialStamp = entity.Timestamp;
            createTime = entity.CreateTime;
            Assert.NotEqual(Guid.Empty, initialStamp);
            Assert.NotNull(createTime);

            entity.DefinitionVersion = "2";
            entity.DisplayName = "Updated before insert";
            Assert.Equal(MutationResult.Applied, await mutation.UpdateAsync(entity, uow));
            Assert.Equal(initialStamp, entity.Timestamp);
            Assert.Equal(createTime, entity.CreateTime);
            Assert.Null(await FindProcessAsync(host, entity.CanonicalName!));
            Assert.Empty(host.Recorder.Calls);
            Assert.Empty(host.Recorder.Mutations);

            await uow.CommitAsync();
            Assert.Equal(initialStamp, entity.Timestamp);
        }

        var stored = await FindProcessAsync(host, entity.CanonicalName!);
        Assert.NotNull(stored);
        Assert.Equal("2", stored.DefinitionVersion);
        Assert.Equal("Updated before insert", stored.DisplayName);
        Assert.Equal(entity.Name, stored.Name);
        Assert.Equal(initialStamp, stored.Timestamp);
        Assert.Equal(createTime, stored.CreateTime);
        Assert.Equal(entity.UpdateTime, stored.UpdateTime);
        var firstResource = host.Recorder.Calls.IndexOf("resource");
        Assert.True(firstResource > 0);
        Assert.All(host.Recorder.Calls.Take(firstResource), call => Assert.Equal("repository", call));
        Assert.Equal(new[] { "resource", "resource" }, host.Recorder.Calls.Skip(firstResource));
        Assert.Equal(new[] { (Operations.Create, "1"), (Operations.Update, "2") }, host.Recorder.Mutations);
    }

    [Fact]
    public async Task Create_TwoTypesShareOuterCommit_NothingVisibleBeforeCommit() {
        await using var host = await CreateHostAsync();

        SchemataProcess a;
        SchemataJob     b;
        using (var scope = host.Root.CreateScope()) {
            var uow       = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
            var mutationA = scope.ServiceProvider.GetRequiredService<IResourceMutation<SchemataProcess>>();
            var mutationB = scope.ServiceProvider.GetRequiredService<IResourceMutation<SchemataJob>>();

            a = NewProcess();
            b = NewJob();

            Assert.Equal(MutationResult.Applied, await mutationA.CreateAsync(a, uow));
            Assert.Equal(MutationResult.Applied, await mutationB.CreateAsync(b, uow));

            Assert.Equal(0, await CountProcessesAsync(host));
            Assert.Equal(0, await CountJobsAsync(host));

            await uow.CommitAsync();
        }

        var process = await FindProcessAsync(host, a.CanonicalName!);
        var job     = await FindJobAsync(host, b.CanonicalName!);
        Assert.NotNull(process);
        Assert.NotNull(job);
        Assert.Equal(new[] { a.CanonicalName, b.CanonicalName }, new[] { process!.CanonicalName, job!.CanonicalName });
    }

    [Fact]
    public async Task Create_WhenBlockedAfterAdvisorWork_ReturnsNoWriteAndPersistsNothing() {
        await using var host = await CreateHostAsync();
        host.Recorder.BlockAdds = true;

        MutationResult result;
        using (var scope = host.Root.CreateScope()) {
            var mutation = scope.ServiceProvider.GetRequiredService<IResourceMutation<SchemataProcess>>();
            result = await mutation.CreateAsync(NewProcess());
        }

        Assert.Equal(MutationResult.NoWrite, result);
        Assert.Equal(0, await CountProcessesAsync(host));
    }

    [Fact]
    public async Task Create_WhenStageThrows_PropagatesAndPersistsNothing() {
        await using var host = await CreateHostAsync();
        host.Recorder.ThrowOnAdd = true;

        using (var scope = host.Root.CreateScope()) {
            var mutation = scope.ServiceProvider.GetRequiredService<IResourceMutation<SchemataProcess>>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => mutation.CreateAsync(NewProcess()));
        }

        Assert.Equal(0, await CountProcessesAsync(host));
    }

    [Fact]
    public async Task Create_WhenCommitFails_ThrowsAndRollsBack() {
        await using var host = await CreateHostAsync();

        var seeded = NewProcess();
        using (var scope = host.Root.CreateScope()) {
            var mutation = scope.ServiceProvider.GetRequiredService<IResourceMutation<SchemataProcess>>();
            Assert.Equal(MutationResult.Applied, await mutation.CreateAsync(seeded));
        }

        host.Recorder.SuppressUniqueness = true;
        using (var scope = host.Root.CreateScope()) {
            var mutation  = scope.ServiceProvider.GetRequiredService<IResourceMutation<SchemataProcess>>();
            var duplicate = NewProcess();
            duplicate.Uid = seeded.Uid;

            await Assert.ThrowsAsync<AlreadyExistsException>(() => mutation.CreateAsync(duplicate));
        }

        Assert.Equal(1, await CountProcessesAsync(host));
    }

    [Fact]
    public async Task Create_WhenOuterRollsBack_PersistsNothing_AndFreshMutationCommits() {
        await using var host = await CreateHostAsync();

        using (var scope = host.Root.CreateScope()) {
            var uow      = scope.ServiceProvider.GetRequiredService<IUnitOfWork<TestDbContext>>();
            var mutation = scope.ServiceProvider.GetRequiredService<IResourceMutation<SchemataProcess>>();

            var rolledBack = NewProcess();
            Assert.Equal(MutationResult.Applied, await mutation.CreateAsync(rolledBack, uow));
            await uow.RollbackAsync();
        }

        // The rolled-back row is invisible to a fresh reader.
        Assert.Equal(0, await CountProcessesAsync(host));

        // A rolled-back unit of work is one-shot: the next independent operation runs through a
        // fresh scope, repository, and self-committing mutation.
        string canonicalName;
        using (var scope = host.Root.CreateScope()) {
            var mutation = scope.ServiceProvider.GetRequiredService<IResourceMutation<SchemataProcess>>();
            var kept     = NewProcess();
            Assert.Equal(MutationResult.Applied, await mutation.CreateAsync(kept));
            canonicalName = kept.CanonicalName!;
        }

        Assert.NotNull(await FindProcessAsync(host, canonicalName));
        Assert.Equal(1, await CountProcessesAsync(host));
    }

    [Fact]
    public async Task Delete_SoftDeleteEntity_PropagatesTheNestedUpdateResult() {
        await using var host = await CreateHostAsync();

        SchemataProcess entity;
        using (var scope = host.Root.CreateScope()) {
            var mutation = scope.ServiceProvider.GetRequiredService<IResourceMutation<SchemataProcess>>();
            entity = NewProcess();
            Assert.Equal(MutationResult.Applied, await mutation.CreateAsync(entity));
        }

        // The soft-delete advisor handles the remove and stages an update on the same repository:
        // the nested write's result propagates, and the row persists with DeleteTime set.
        MutationResult result;
        using (var scope = host.Root.CreateScope()) {
            var mutation = scope.ServiceProvider.GetRequiredService<IResourceMutation<SchemataProcess>>();
            result = await mutation.DeleteAsync(entity);
        }

        Assert.Equal(MutationResult.Applied, result);
        Assert.Equal(0, await CountProcessesAsync(host));

        using (var scope = host.Root.CreateScope()) {
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<SchemataProcess>>();
            using (repository.SuppressQuerySoftDelete()) {
                var row = await repository.FirstOrDefaultAsync<SchemataProcess>(
                    q => q.Where(p => p.CanonicalName == entity.CanonicalName));
                Assert.NotNull(row);
                Assert.NotNull(row!.DeleteTime);
            }
        }
    }

    [Fact]
    public async Task Delete_Expunge_PhysicallyRemovesAndReturnsApplied() {
        await using var host = await CreateHostAsync();

        SchemataProcess entity;
        using (var scope = host.Root.CreateScope()) {
            var mutation = scope.ServiceProvider.GetRequiredService<IResourceMutation<SchemataProcess>>();
            entity = NewProcess();
            Assert.Equal(MutationResult.Applied, await mutation.CreateAsync(entity));
        }

        using (var scope = host.Root.CreateScope()) {
            var mutation = scope.ServiceProvider.GetRequiredService<IResourceMutation<SchemataProcess>>();
            Assert.Equal(MutationResult.Applied, await mutation.DeleteAsync(entity, operation: Operations.Expunge));
        }

        using (var scope = host.Root.CreateScope()) {
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<SchemataProcess>>();
            using (repository.SuppressQuerySoftDelete()) {
                Assert.Equal(0, await repository.CountAsync<SchemataProcess>(q => q.Where(p => p.Uid == entity.Uid)));
            }
        }
    }

    [Fact]
    public async Task Commit_WithNoWrites_SendsNoNotification() {
        await using var host = await CreateHostAsync();

        using (var scope = host.Root.CreateScope()) {
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<SchemataProcess>>();
            await repository.CommitAsync();

            var uow = repository.Begin();
            await uow.CommitAsync();
        }

        Assert.Empty(host.Recorder.Calls);
    }

    [Fact]
    public async Task CommitSinks_RunRepositorySegmentBeforeResourceSegment() {
        await using var host = await CreateHostAsync();

        using (var scope = host.Root.CreateScope()) {
            var mutation = scope.ServiceProvider.GetRequiredService<IResourceMutation<SchemataProcess>>();
            Assert.Equal(MutationResult.Applied, await mutation.CreateAsync(NewProcess()));
        }

        Assert.Equal(new[] { "repository", "resource" }, host.Recorder.Calls);
    }

    [Fact]
    public async Task FailingRepositorySink_DoesNotBlockResourceSink_AndCommitStaysDurable() {
        await using var host = await CreateHostAsync();
        host.Recorder.FailRepositorySink = true;

        string canonicalName;
        using (var scope = host.Root.CreateScope()) {
            var mutation = scope.ServiceProvider.GetRequiredService<IResourceMutation<SchemataProcess>>();
            var entity   = NewProcess();

            // The save already committed; the failing sink propagates without replaying anything.
            await Assert.ThrowsAsync<InvalidOperationException>(() => mutation.CreateAsync(entity));
            canonicalName = entity.CanonicalName!;
        }

        Assert.NotNull(await FindProcessAsync(host, canonicalName));
        Assert.Equal("resource", Assert.Single(host.Recorder.Calls));
        Assert.Equal((Operations.Create, "1"), Assert.Single(host.Recorder.Mutations));
    }

    private static SchemataProcess NewProcess() {
        return new() {
            DefinitionName    = "definition",
            DefinitionVersion = "1",
        };
    }

    private static SchemataJob NewJob() {
        var key = Guid.NewGuid().ToString("N");
        return new() {
            Name           = key,
            Key            = key,
            ScheduleType   = ScheduleType.OneTime,
            NextRunTime    = DateTime.UtcNow.AddDays(1),
        };
    }

    private static async Task<int> CountProcessesAsync(Host host) {
        using var scope      = host.Root.CreateScope();
        var       repository = scope.ServiceProvider.GetRequiredService<IRepository<SchemataProcess>>();
        return await repository.CountAsync<SchemataProcess>(null);
    }

    private static async Task<int> CountJobsAsync(Host host) {
        using var scope      = host.Root.CreateScope();
        var       repository = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJob>>();
        return await repository.CountAsync<SchemataJob>(null);
    }

    private static async Task<SchemataProcess?> FindProcessAsync(Host host, string canonicalName) {
        using var scope      = host.Root.CreateScope();
        var       repository = scope.ServiceProvider.GetRequiredService<IRepository<SchemataProcess>>();
        return await repository.FirstOrDefaultAsync<SchemataProcess>(q => q.Where(p => p.CanonicalName == canonicalName));
    }

    private static async Task<SchemataJob?> FindJobAsync(Host host, string canonicalName) {
        using var scope      = host.Root.CreateScope();
        var       repository = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJob>>();
        return await repository.FirstOrDefaultAsync<SchemataJob>(q => q.Where(j => j.CanonicalName == canonicalName));
    }

    private static async Task<Host> CreateHostAsync() {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var recorder = new Recorder();
        var services = new ServiceCollection();
        services.AddSingleton(recorder);

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IRepositoryAddAdvisor<SchemataProcess>, FlowTestCreation.NameAdvisor<SchemataProcess>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataProcess>, BlockingAddAdvisor>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataProcess>, ThrowingAddAdvisor>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataProcess>, SuppressingUniquenessAdvisor>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryCommittedAdvisor<SchemataProcess>, RecordingCommittedAdvisor>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IResourceMutationCommittedAdvisor<SchemataProcess>, RecordingMutationAdvisor>());

        services.AddDbContextFactory<TestDbContext>(opts => opts.UseSqlite(connection)
                                                       .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());

        services.AddRepository<SchemataProcess, EfCoreRepository<TestDbContext, SchemataProcess>>();
        services.AddRepository<SchemataJob, EfCoreRepository<TestDbContext, SchemataJob>>();

        services.AddScoped<IUnitOfWork<TestDbContext>, EfCoreUnitOfWork<TestDbContext>>();

        var root = services.BuildServiceProvider();

        await using (var scope = root.CreateAsyncScope()) {
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await db.Database.EnsureCreatedAsync();
        }

        return new(connection, root, recorder);
    }

    private sealed class Recorder
    {
        public List<string> Calls { get; } = [];

        public List<(Operations Operation, string Version)> Mutations { get; } = [];

        public bool BlockAdds { get; set; }

        public bool ThrowOnAdd { get; set; }

        public bool FailRepositorySink { get; set; }

        public bool SuppressUniqueness { get; set; }
    }

    private sealed class Host(SqliteConnection connection, ServiceProvider root, Recorder recorder) : IAsyncDisposable
    {
        public ServiceProvider Root { get; } = root;

        public Recorder Recorder { get; } = recorder;

        public async ValueTask DisposeAsync() {
            await Root.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class BlockingAddAdvisor(Recorder recorder) : IRepositoryAddAdvisor<SchemataProcess>
    {
        public int Order => int.MaxValue;

        public Task<AdviseResult> AdviseAsync(
            AdviceContext                context,
            IRepository<SchemataProcess> repository,
            SchemataProcess              entity,
            CancellationToken            ct
        ) {
            return Task.FromResult(recorder.BlockAdds ? AdviseResult.Block : AdviseResult.Continue);
        }
    }

    private sealed class ThrowingAddAdvisor(Recorder recorder) : IRepositoryAddAdvisor<SchemataProcess>
    {
        public int Order => int.MaxValue - 1;

        public Task<AdviseResult> AdviseAsync(
            AdviceContext                context,
            IRepository<SchemataProcess> repository,
            SchemataProcess              entity,
            CancellationToken            ct
        ) {
            if (recorder.ThrowOnAdd) {
                throw new InvalidOperationException("Staging failed.");
            }

            return Task.FromResult(AdviseResult.Continue);
        }
    }

    private sealed class SuppressingUniquenessAdvisor(Recorder recorder) : IRepositoryAddAdvisor<SchemataProcess>
    {
        public int Order => int.MinValue;

        public Task<AdviseResult> AdviseAsync(
            AdviceContext                context,
            IRepository<SchemataProcess> repository,
            SchemataProcess              entity,
            CancellationToken            ct
        ) {
            if (recorder.SuppressUniqueness) {
                context.Use<UniquenessSuppressed>();
            }

            return Task.FromResult(AdviseResult.Continue);
        }
    }

    private sealed class RecordingCommittedAdvisor(Recorder recorder) : IRepositoryCommittedAdvisor<SchemataProcess>
    {
        // The cache eviction advisor's order within the repository segment.
        public int Order => SchemataConstants.Orders.Max;

        public Task<AdviseResult> AdviseAsync(
            AdviceContext                context,
            IRepository<SchemataProcess> resource,
            CancellationToken            ct
        ) {
            if (recorder.FailRepositorySink) {
                throw new InvalidOperationException("Repository sink failed.");
            }

            recorder.Calls.Add("repository");

            return Task.FromResult(AdviseResult.Continue);
        }
    }

    private sealed class RecordingMutationAdvisor(Recorder recorder) : IResourceMutationCommittedAdvisor<SchemataProcess>
    {
        // The pending-events advisor's resource-segment position.
        public int Order => SchemataConstants.Orders.Max - 1_000;

        public Func<CancellationToken, Task>? Prepare(SchemataProcess entity, Operations operation) {
            var version = entity.DefinitionVersion;
            return _ => {
                recorder.Calls.Add("resource");
                recorder.Mutations.Add((operation, version));
                return Task.CompletedTask;
            };
        }
    }
}
