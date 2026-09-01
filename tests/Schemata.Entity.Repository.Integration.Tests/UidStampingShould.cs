using System;
using System.Threading.Tasks;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository.Integration.Tests.Fixtures;
using Schemata.Event.Skeleton.Entities;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Push.Skeleton.Entities;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Entity.Repository.Integration.Tests;

[Trait("Category", "Integration")]
public class UidStampingShould : IAsyncLifetime
{
    private readonly RepositoryFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task Add_SchemataProcess_AssignsNonEmptyUid() {
        var entity = new SchemataProcess {
            DefinitionName = "test",
            Name          = "process-1",
        };

        var (repository, scope) = _fixture.CreateScope<SchemataProcess>();
        using (scope) {
            await repository.AddAsync(entity);
            await repository.CommitAsync();
        }

        Assert.NotEqual(Guid.Empty, entity.Uid);
    }

    [Fact]
    public async Task Add_SchemataProcessToken_AssignsNonEmptyUid() {
        var entity = new SchemataProcessToken {
            Process   = "process-1",
            ScopeName = "scope",
            StateName = "start",
            Name      = "token-1",
        };

        var (repository, scope) = _fixture.CreateScope<SchemataProcessToken>();
        using (scope) {
            await repository.AddAsync(entity);
            await repository.CommitAsync();
        }

        Assert.NotEqual(Guid.Empty, entity.Uid);
    }

    [Fact]
    public async Task Add_SchemataProcessTransition_AssignsNonEmptyUid() {
        var entity = new SchemataProcessTransition {
            Process   = "process-1",
            Token     = "process-1/tokens/token-1",
            Kind      = TransitionKind.Move,
            Previous  = "start",
            Posterior = "next",
            Event     = "move",
            Name      = "transition-1",
        };

        var (repository, scope) = _fixture.CreateScope<SchemataProcessTransition>();
        using (scope) {
            await repository.AddAsync(entity);
            await repository.CommitAsync();
        }

        Assert.NotEqual(Guid.Empty, entity.Uid);
    }

    [Fact]
    public async Task Add_SchemataJob_AssignsNonEmptyUid() {
        var entity = new SchemataJob {
            JobKey         = "key",
            Name           = "job-1",
            ScheduleType   = ScheduleType.Periodic,
            IntervalTicks  = 60_000_000_000,
        };

        var (repository, scope) = _fixture.CreateScope<SchemataJob>();
        using (scope) {
            await repository.AddAsync(entity);
            await repository.CommitAsync();
        }

        Assert.NotEqual(Guid.Empty, entity.Uid);
    }

    [Fact]
    public async Task Add_SchemataJobExecution_AssignsNonEmptyUid() {
        var entity = new SchemataJobExecution {
            JobKey = "key",
            Name   = "execution-1",
        };

        var (repository, scope) = _fixture.CreateScope<SchemataJobExecution>();
        using (scope) {
            await repository.AddAsync(entity);
            await repository.CommitAsync();
        }

        Assert.NotEqual(Guid.Empty, entity.Uid);
    }

    [Fact]
    public async Task Add_SchemataPushSubscription_AssignsNonEmptyUid() {
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

        Assert.NotEqual(Guid.Empty, entity.Uid);
    }

    [Fact]
    public async Task Add_SchemataEvent_AssignsUidBeforeMissingNameIsRejected() {
        var entity = new SchemataEvent {
            EventType = "order.placed",
            Payload   = "{}",
        };

        var (repository, scope) = _fixture.CreateScope<SchemataEvent>();
        using (scope) {
            await Assert.ThrowsAsync<ValidationException>(() => repository.AddAsync(entity));
        }

        Assert.NotEqual(Guid.Empty, entity.Uid);
    }
}