using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;
using Schemata.Flow.Repository;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;

namespace Schemata.Flow.Tests;

/// <summary>
///     Characterizes <see cref="RepositoryFlowEffectRecorder" /> against a store that cannot observe
///     rows staged inside the current unit of work — the provider shape the staged map exists for.
/// </summary>
public class RepositoryFlowEffectRecorderShould
{
    [Fact]
    public async Task Reuse_The_Staged_Row_When_The_Same_Request_Id_Is_Recorded_Twice() {
        var created    = new List<SchemataFlowEffectIntent>();
        var repository = Repository();
        var recorder   = new RepositoryFlowEffectRecorder(repository.Object, Mutation(created).Object);
        var unit       = new Mock<IUnitOfWork>();

        var first  = await recorder.RecordIntentAsync(Intent("processes/p1/effects/0"), unit.Object, CancellationToken.None);
        var second = await recorder.RecordIntentAsync(Intent("processes/p1/effects/0"), unit.Object, CancellationToken.None);

        Assert.Equal(first.RequestId, second.RequestId);
        Assert.Equal(first.Process, second.Process);
        Assert.Equal(first.Token, second.Token);
        Assert.Equal(first.Task, second.Task);
        Assert.Equal(FlowEffectState.Recorded, second.State);
        Assert.Single(created);
        repository.Verify(r => r.Join(unit.Object), Times.Exactly(2));
    }

    [Fact]
    public async Task Surface_The_Staged_Row_State_After_An_In_Flight_Settle() {
        var created  = new List<SchemataFlowEffectIntent>();
        var recorder = new RepositoryFlowEffectRecorder(Repository().Object, Mutation(created).Object);
        var unit     = new Mock<IUnitOfWork>();

        await recorder.RecordIntentAsync(Intent("processes/p1/effects/0"), unit.Object, CancellationToken.None);
        await recorder.RecordOutcomeUnknownAsync("processes/p1/effects/0", unit.Object, CancellationToken.None);

        var repeat = await recorder.RecordIntentAsync(Intent("processes/p1/effects/0"), unit.Object, CancellationToken.None);

        Assert.Equal(FlowEffectState.OutcomeUnknown, repeat.State);
        Assert.Single(created);
    }

    [Fact]
    public async Task Keep_Returning_The_Staged_Authoritative_Record_Through_Completion() {
        var created  = new List<SchemataFlowEffectIntent>();
        var recorder = new RepositoryFlowEffectRecorder(Repository().Object, Mutation(created).Object);
        var unit     = new Mock<IUnitOfWork>();

        await recorder.RecordIntentAsync(Intent("processes/p1/effects/0"), unit.Object, CancellationToken.None);
        await recorder.RecordCompletionAsync("processes/p1/effects/0", unit.Object, CancellationToken.None);

        var repeat = await recorder.RecordIntentAsync(Intent("processes/p1/effects/0"), unit.Object, CancellationToken.None);

        Assert.Equal(FlowEffectState.Completed, repeat.State);
        Assert.Single(created);
    }

    [Fact]
    public async Task Return_The_Stored_Record_When_The_Request_Id_Already_Committed() {
        var committed = new SchemataFlowEffectIntent {
            RequestId = "processes/p1/effects/0",
            Process   = "processes/p1",
            Token     = "processes/p1/tokens/t1",
            Task      = "Enter_Call",
            State     = FlowEffectState.Completed,
        };
        var mutation = new Mock<IResourceMutation<SchemataFlowEffectIntent>>();
        var recorder = new RepositoryFlowEffectRecorder(Repository(committed).Object, mutation.Object);
        var unit     = new Mock<IUnitOfWork>();

        var record = await recorder.RecordIntentAsync(Intent("processes/p1/effects/0"), unit.Object, CancellationToken.None);

        Assert.Equal(FlowEffectState.Completed, record.State);
        Assert.Equal("Enter_Call", record.Task);
        mutation.Verify(
            m => m.CreateAsync(It.IsAny<SchemataFlowEffectIntent>(), It.IsAny<IUnitOfWork?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static FlowEffectIntent Intent(string requestId) {
        return new() {
            RequestId = requestId,
            Process   = "processes/p1",
            Token     = "processes/p1/tokens/t1",
            Task      = "Enter_Call",
        };
    }

    private static Mock<IRepository<SchemataFlowEffectIntent>> Repository(params SchemataFlowEffectIntent[] rows) {
        var data       = rows.ToList();
        var repository = new Mock<IRepository<SchemataFlowEffectIntent>>();
        repository.Setup(r => r.Join(It.IsAny<IUnitOfWork>()));
        repository.Setup(r => r.FirstOrDefaultAsync(
                          It.IsAny<Func<IQueryable<SchemataFlowEffectIntent>, IQueryable<SchemataFlowEffectIntent>>>(),
                          It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                  .Returns((Func<IQueryable<SchemataFlowEffectIntent>, IQueryable<SchemataFlowEffectIntent>> predicate,
                            CancellationToken _, QueryOptions _) => new ValueTask<SchemataFlowEffectIntent?>(
                                predicate(data.AsQueryable()).FirstOrDefault()));
        return repository;
    }

    private static Mock<IResourceMutation<SchemataFlowEffectIntent>> Mutation(List<SchemataFlowEffectIntent> created) {
        var mutation = new Mock<IResourceMutation<SchemataFlowEffectIntent>>();
        mutation.Setup(m => m.CreateAsync(
                        It.IsAny<SchemataFlowEffectIntent>(), It.IsAny<IUnitOfWork?>(), It.IsAny<CancellationToken>()))
                .Callback((SchemataFlowEffectIntent row, IUnitOfWork? _, CancellationToken _) => created.Add(row))
                .ReturnsAsync(MutationResult.Applied);
        mutation.Setup(m => m.UpdateAsync(
                        It.IsAny<SchemataFlowEffectIntent>(), It.IsAny<IUnitOfWork?>(), It.IsAny<Operations>(),
                        It.IsAny<CancellationToken>()))
                .ReturnsAsync(MutationResult.Applied);
        return mutation;
    }
}
