using System;
using System.Threading.Tasks;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository.Integration.Tests.Fixtures;
using Schemata.Event.Skeleton.Entities;
using Schemata.Flow.Skeleton.Entities;
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