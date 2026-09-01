using System.Threading.Tasks;
using Schemata.Entity.Repository.Integration.Tests.Fixtures;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Push.Skeleton.Entities;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Entity.Repository.Integration.Tests;

[Trait("Category", "Integration")]
public class CanonicalNamePopulationShould : IAsyncLifetime
{
    private readonly RepositoryFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task Add_SchemataProcess_PopulatesCanonicalNameFromCollectionAndName() {
        var entity = new SchemataProcess {
            DefinitionName = "test",
            Name          = "process-1",
        };

        var (repository, scope) = _fixture.CreateScope<SchemataProcess>();
        using (scope) {
            await repository.AddAsync(entity);
            await repository.CommitAsync();
        }

        Assert.Equal("processes/process-1", entity.CanonicalName);
    }

    [Fact]
    public async Task Add_SchemataJob_PopulatesCanonicalNameFromCollectionAndName() {
        var entity = new SchemataJob {
            JobKey        = "key",
            Name          = "job-1",
            ScheduleType  = ScheduleType.Periodic,
            IntervalTicks = 60_000_000_000,
        };

        var (repository, scope) = _fixture.CreateScope<SchemataJob>();
        using (scope) {
            await repository.AddAsync(entity);
            await repository.CommitAsync();
        }

        Assert.Equal("jobs/job-1", entity.CanonicalName);
    }

    [Fact]
    public async Task Add_SchemataJobExecution_PopulatesCanonicalNameFromCollectionAndName() {
        var entity = new SchemataJobExecution {
            JobKey = "key",
            Name   = "execution-1",
        };

        var (repository, scope) = _fixture.CreateScope<SchemataJobExecution>();
        using (scope) {
            await repository.AddAsync(entity);
            await repository.CommitAsync();
        }

        Assert.Equal("operations/execution-1", entity.CanonicalName);
    }

    [Fact]
    public async Task Add_SchemataPushSubscription_PopulatesCanonicalNameFromCollectionAndName() {
        var entity = new SchemataPushSubscription {
            Provider    = "fcm",
            ProviderKey = "token-1",
            Owner       = "users/alice",
            Name        = "subscription-1",
        };

        var (repository, scope) = _fixture.CreateScope<SchemataPushSubscription>();
        using (scope) {
            await repository.AddAsync(entity);
            await repository.CommitAsync();
        }

        Assert.Equal("pushSubscriptions/subscription-1", entity.CanonicalName);
    }
}