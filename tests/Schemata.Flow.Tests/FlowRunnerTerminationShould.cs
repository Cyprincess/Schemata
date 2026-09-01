using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Flow.Foundation;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;

namespace Schemata.Flow.Tests;

/// <summary>
///     Characterization of <see cref="IFlowRunner.TerminateAsync" /> and
///     <see cref="IFlowRunner.CancelTokenAsync" />, the two facade methods no other test drove.
/// </summary>
/// <remarks>
///     These pin the observable facade behaviour ahead of the Flow command-isation, which moves the
///     orchestration out of <c>FlowRunner</c> into request handlers. They must keep passing
///     unchanged through that move — a diff here means the behaviour drifted, not that the test was
///     wrong.
/// </remarks>
public class FlowRunnerTerminationShould
{
    [Fact]
    public async Task Terminate_CancelsEveryToken_AndMarksTheProcessTerminated() {
        var harness = CreateHarness(
            Token("t1", "Waiting", "await-approval"),
            Token("t2", "Active"));

        var snapshot = await harness.Runner.TerminateAsync(harness.Process, null, CancellationToken.None);

        Assert.Equal("Terminated", snapshot.Process.State);
        Assert.All(snapshot.Tokens, t => Assert.Equal("Cancelled", t.State));
        Assert.All(snapshot.Tokens, t => Assert.Null(t.WaitingAtName));
    }

    [Fact]
    public async Task Terminate_RecordsOneTransitionPerToken() {
        var harness = CreateHarness(Token("t1", "Waiting"), Token("t2", "Active"));

        var snapshot = await harness.Runner.TerminateAsync(harness.Process, null, CancellationToken.None);

        Assert.Equal(2, snapshot.Transitions.Count);
    }

    [Fact]
    public async Task Terminate_AlsoCancelsTokensThatWereAlreadyTerminal() {
        // Terminate is unconditional: it does not skip finished tokens, so the post-state is
        // uniform regardless of what each token was doing.
        var harness = CreateHarness(Token("t1", "Completed"), Token("t2", "Waiting"));

        var snapshot = await harness.Runner.TerminateAsync(harness.Process, null, CancellationToken.None);

        Assert.All(snapshot.Tokens, t => Assert.Equal("Cancelled", t.State));
        Assert.Equal(2, snapshot.Transitions.Count);
    }

    [Fact]
    public async Task CancelToken_CancelsOnlyTheTargetToken() {
        var harness = CreateHarness(Token("t1", "Waiting"), Token("t2", "Active"));

        var snapshot = await harness.Runner.CancelTokenAsync(harness.Tokens[0], null, CancellationToken.None);

        Assert.Equal("Cancelled", snapshot.Tokens.Single(t => t.Name == "t1").State);
        Assert.Equal("Active", snapshot.Tokens.Single(t => t.Name == "t2").State);
        Assert.Single(snapshot.Transitions);
    }

    [Fact]
    public async Task CancelToken_LeavesTheProcessRunning_WhileAnotherTokenIsLive() {
        var harness = CreateHarness(Token("t1", "Waiting"), Token("t2", "Active"));

        var snapshot = await harness.Runner.CancelTokenAsync(harness.Tokens[0], null, CancellationToken.None);

        Assert.NotEqual("Cancelled", snapshot.Process.State);
    }

    [Fact]
    public async Task CancelToken_MarksTheProcessCancelled_WhenItRetiresTheLastLiveToken() {
        var harness = CreateHarness(Token("t1", "Waiting"), Token("t2", "Completed"));

        var snapshot = await harness.Runner.CancelTokenAsync(harness.Tokens[0], null, CancellationToken.None);

        Assert.Equal("Cancelled", snapshot.Process.State);
    }

    [Fact]
    public async Task CancelToken_OnAnAlreadyTerminalToken_RejectsWithFailedPrecondition() {
        var harness = CreateHarness(Token("t1", "Completed"), Token("t2", "Active"));

        await Assert.ThrowsAsync<FailedPreconditionException>(
            async () => await harness.Runner.CancelTokenAsync(harness.Tokens[0], null, CancellationToken.None));
    }

    [Fact]
    public async Task CancelToken_WhenTheOwningProcessIsGone_RejectsWithNotFound() {
        var harness = CreateHarness(Token("t1", "Waiting"));

        var orphan = new SchemataProcessToken {
            Name          = "t9",
            CanonicalName = "processes/missing/tokens/t9",
            Process       = "missing",
            State         = "Waiting",
        };

        await Assert.ThrowsAsync<NotFoundException>(
            async () => await harness.Runner.CancelTokenAsync(orphan, null, CancellationToken.None));
    }

    [Fact]
    public async Task CancelToken_ForATokenTheProcessDoesNotOwn_RejectsWithNotFound() {
        var harness = CreateHarness(Token("t1", "Waiting"));

        var stranger = new SchemataProcessToken {
            Name          = "t9",
            CanonicalName = "processes/p1/tokens/t9",
            Process       = "p1",
            State         = "Waiting",
        };

        await Assert.ThrowsAsync<NotFoundException>(
            async () => await harness.Runner.CancelTokenAsync(stranger, null, CancellationToken.None));
    }

    private static SchemataProcessToken Token(string name, string state, string? waitingAt = null) {
        return new() {
            Name          = name,
            CanonicalName = $"processes/p1/tokens/{name}",
            Process       = "p1",
            State         = state,
            WaitingAtName = waitingAt,
        };
    }

    private static Harness CreateHarness(params SchemataProcessToken[] tokens) {
        var registration = new ProcessRegistration {
            Name          = "termination-process",
            Engine        = FlowConstants.Engines.StateMachine,
            Definition    = new TerminationProcess(),
            Configuration = new ProcessConfiguration(),
        };

        var harness = new Harness { Tokens = tokens };

        var registry = new Mock<IProcessRegistry>();
        registry.Setup(r => r.GetRegistration("termination-process")).Returns(registration);

        var engine = new Mock<IFlowRuntime>();

        var process = new SchemataProcess {
            Name           = "p1",
            CanonicalName  = "processes/p1",
            DefinitionName = "termination-process",
            State          = "Running",
        };
        harness.Process = process;

        var processes = Repository(process);
        var tokenRepo = Repository(tokens);
        processes.Setup(r => r.Begin()).Returns(Mock.Of<IUnitOfWork>());

        var collection = new ServiceCollection()
                        .AddLogging()
                        .AddSingleton(registry.Object)
                        .AddSingleton<IOptions<SchemataFlowOptions>>(Options.Create(new SchemataFlowOptions()))
                        .AddSingleton(processes.Object)
                        .AddSingleton(tokenRepo.Object)
                        .AddSingleton(Repository<SchemataProcessTransition>().Object)
                        .AddSingleton(Repository<SchemataProcessSource>().Object)
                        .AddSingleton(Repository<SchemataProcessCompensation>().Object)
                        .AddKeyedSingleton<IFlowRuntime>(FlowConstants.Engines.StateMachine, engine.Object);
        collection.AddSchemataFlow();
        var services = collection.BuildServiceProvider();

        harness.Runner = services.GetRequiredService<FlowRunner>();

        return harness;
    }

    private static Mock<IRepository<T>> Repository<T>(params T[] items)
        where T : class {
        var data       = items.ToList();
        var repository = new Mock<IRepository<T>>();
        repository.Setup(r => r.Join(It.IsAny<IUnitOfWork>()));
        repository.Setup(r => r.Begin()).Returns(Mock.Of<IUnitOfWork>());
        repository.Setup(r => r.AddAsync(It.IsAny<T>(), It.IsAny<CancellationToken>()))
                  .Returns((T entity, CancellationToken _) => {
                      data.Add(entity);
                      return Task.CompletedTask;
                  });
        repository.Setup(r => r.UpdateAsync(It.IsAny<T>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(r => r.ListAsync<T>(It.IsAny<Func<IQueryable<T>, IQueryable<T>>>(), It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<T>, IQueryable<T>> predicate, CancellationToken _) =>
                               Async(predicate(data.AsQueryable()).ToList()));
        repository.Setup(r => r.SingleOrDefaultAsync(It.IsAny<Func<IQueryable<T>, IQueryable<T>>>(), It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<T>, IQueryable<T>> predicate, CancellationToken _) =>
                               new ValueTask<T?>(predicate(data.AsQueryable()).SingleOrDefault()));
        repository.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Func<IQueryable<T>, IQueryable<T>>>(), It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<T>, IQueryable<T>> predicate, CancellationToken _) =>
                               new ValueTask<T?>(predicate(data.AsQueryable()).FirstOrDefault()));
        return repository;
    }

    private static async IAsyncEnumerable<T> Async<T>(IEnumerable<T> items) {
        foreach (var item in items) {
            yield return item;
        }

        await Task.CompletedTask;
    }

    private sealed class Harness
    {
        public FlowRunner Runner { get; set; } = null!;

        public SchemataProcess Process { get; set; } = null!;

        public SchemataProcessToken[] Tokens { get; init; } = [];
    }

    private sealed class TerminationProcess : ProcessDefinition;
}
