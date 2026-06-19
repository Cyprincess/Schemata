using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Schemata.Entity.Repository;
using Schemata.Scheduling.Foundation.Internal;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Tests;

public class AqnJobKeyMigrationShould
{
    [Fact]
    public async Task Migration_PopulatesJobKey_FromValidAqn() {
        var row      = JobWithAqn(typeof(MigrationJob).AssemblyQualifiedName!);
        var jobs     = Repository([row]);
        var registry = new DefaultScheduledJobRegistry();
        registry.Register<MigrationJob>("jobs:migration");

        await AqnJobKeyMigration.RunAsync(jobs.Object, registry, CancellationToken.None);

        Assert.Equal("jobs:migration", row.JobKey);
        jobs.Verify(r => r.UpdateAsync(row, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Migration_SkipsUnresolvableAqn() {
        var row      = JobWithAqn("Missing.Type, Missing.Assembly");
        var jobs     = Repository([row]);
        var registry = new DefaultScheduledJobRegistry();

        await AqnJobKeyMigration.RunAsync(jobs.Object, registry, CancellationToken.None);

        Assert.Null(row.JobKey);
        jobs.Verify(r => r.UpdateAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Migration_SkipsRowWithJobKeySet() {
        var row = JobWithAqn(typeof(MigrationJob).AssemblyQualifiedName!);
        row.JobKey = "already-migrated";
        var jobs     = Repository([row]);
        var registry = new DefaultScheduledJobRegistry();
        registry.Register<MigrationJob>("jobs:migration");

        await AqnJobKeyMigration.RunAsync(jobs.Object, registry, CancellationToken.None);

        Assert.Equal("already-migrated", row.JobKey);
        jobs.Verify(r => r.UpdateAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Migration_ResolvableTypeMissingRegistryKey_Skips() {
        var row      = JobWithAqn(typeof(MigrationJob).AssemblyQualifiedName!);
        var jobs     = Repository([row]);
        var registry = new DefaultScheduledJobRegistry();
        // The AQN resolves to a real CLR type while the registry omits its key.

        await AqnJobKeyMigration.RunAsync(jobs.Object, registry, CancellationToken.None);

        Assert.Null(row.JobKey);
        jobs.Verify(r => r.UpdateAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Mock<IRepository<SchemataJob>> Repository(IReadOnlyCollection<SchemataJob> rows) {
        var repository = new Mock<IRepository<SchemataJob>>();
        repository.Setup(r => r.ListAsync(
                             It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>>(),
                             It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>> query, CancellationToken _)
                               => ToAsync(query(rows.AsQueryable())));
        repository.Setup(r => r.UpdateAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        repository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return repository;
    }

    private static SchemataJob JobWithAqn(string aqn) {
        var job = new SchemataJob();
        typeof(SchemataJob).GetProperty("JobType")!.SetValue(job, aqn);
        return job;
    }

    private static async IAsyncEnumerable<SchemataJob> ToAsync(IEnumerable<SchemataJob> rows) {
        foreach (var row in rows) {
            yield return row;
            await Task.CompletedTask;
        }
    }

    #region Nested type: MigrationJob

    private sealed class MigrationJob : IScheduledJob
    {
        #region IScheduledJob Members

        public Task ExecuteAsync(JobContext context, CancellationToken ct) { return Task.CompletedTask; }

        #endregion
    }

    #endregion
}
