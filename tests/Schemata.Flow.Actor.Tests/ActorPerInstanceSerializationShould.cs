using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Flow.Actor.Tests.Fixtures;
using Schemata.Flow.Foundation;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using CompleteActivityRequest = Schemata.Flow.Foundation.Commands.CompleteActivityRequest;
using Schemata.Messaging.Skeleton;
using Xunit;

namespace Schemata.Flow.Actor.Tests;

/// <summary>
///     Concurrency acceptance for the Flow.Actor bridge (spec §8.8 M5.2). One hundred tasks aligned
///     on a <see cref="Barrier" /> fire <see cref="CompleteActivityRequest" /> at the same process
///     instance simultaneously, each from its own DI scope. With the bridge installed, the actor's
///     per-instance mailbox serializes every turn: zero <see cref="AbortedException" /> conflicts,
///     exactly one real transition recorded, and every non-winning attempt returns the state
///     machine's own idempotent no-op snapshot (its token is reloaded past the activity by the time
///     that turn runs) rather than a race artifact. A control-group case without the bridge proves
///     the same harness genuinely produces conflicts when nothing serializes it (RFC escape clause:
///     an always-zero control group means the test never manufactured contention in the first
///     place).
/// </summary>
public sealed class ActorPerInstanceSerializationShould
{
    private const int ConcurrentTaskCount = 100;
    private static readonly TimeSpan CompletionDeadline = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task CompleteConcurrently_ThroughFacade_SerializesWithoutConflicts() {
        await RunActorScenarioAsync(EntryPoint.Facade);
    }

    [Fact]
    public async Task CompleteConcurrently_ThroughRequestDispatcher_SerializesWithoutConflicts() {
        await RunActorScenarioAsync(EntryPoint.RequestDispatcher);
    }

    [Fact]
    public async Task CompleteConcurrently_ThroughCommandDispatcher_SerializesWithoutConflicts() {
        await RunActorScenarioAsync(EntryPoint.CommandDispatcher);
    }

    [Fact]
    public async Task CompleteConcurrently_WithoutActorBridge_ProducesConcurrencyConflicts() {
        await using var harness = await ActorConcurrencyHarness.BuildAsync(withActor: false);
        var (canonicalName, _) = await StartProcessAsync(harness.Root);

        var outcome = await RunConcurrentCompletionsAsync(harness.Root, canonicalName, EntryPoint.Facade);

        Assert.True(
            outcome.ConflictCount > 0,
            "Control group produced zero IConcurrency conflicts: the harness is not manufacturing " +
            "genuine contention, so the actor-enabled cases above prove nothing. The race construction " +
            "must be strengthened (RFC §8.8 escape clause).");
    }

    [Fact]
    public async Task CompleteThroughActor_RunsCommandAdvisorExactlyOncePerDispatch() {
        await using var harness = await ActorConcurrencyHarness.BuildAsync(withActor: true);
        var (canonicalName, _) = await StartProcessAsync(harness.Root);

        await using var scope      = harness.Root.CreateAsyncScope();
        var             dispatcher = scope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
        _ = await dispatcher.SendAsync<CompleteActivityRequest, ProcessSnapshot>(
            new(canonicalName, null, null), CancellationToken.None);

        Assert.Equal(1, harness.Advisor.InvocationCount);
    }

    [Fact]
    public async Task Complete_WithAndWithoutActorBridge_ProducesEquivalentSnapshotShape() {
        await using var bare   = await ActorConcurrencyHarness.BuildAsync(withActor: false);
        await using var actor  = await ActorConcurrencyHarness.BuildAsync(withActor: true);
        var (bareName, _)  = await StartProcessAsync(bare.Root);
        var (actorName, _) = await StartProcessAsync(actor.Root);

        await using var bareScope  = bare.Root.CreateAsyncScope();
        await using var actorScope = actor.Root.CreateAsyncScope();

        var bareSnapshot = await bareScope.ServiceProvider.GetRequiredService<IRequestDispatcher>()
                                           .SendAsync<CompleteActivityRequest, ProcessSnapshot>(
                                               new(bareName, null, null), CancellationToken.None);
        var actorSnapshot = await actorScope.ServiceProvider.GetRequiredService<IRequestDispatcher>()
                                             .SendAsync<CompleteActivityRequest, ProcessSnapshot>(
                                                 new(actorName, null, null), CancellationToken.None);

        Assert.Equal(bareSnapshot.Process.DefinitionName, actorSnapshot.Process.DefinitionName);
        Assert.Equal(bareSnapshot.Process.State, actorSnapshot.Process.State);
        Assert.Equal(bareSnapshot.Tokens.Count, actorSnapshot.Tokens.Count);
        Assert.Equal(bareSnapshot.Tokens[0].StateName, actorSnapshot.Tokens[0].StateName);
        Assert.Equal(bareSnapshot.Transitions.Count, actorSnapshot.Transitions.Count);
    }

    private static async Task RunActorScenarioAsync(EntryPoint entry) {
        await using var harness = await ActorConcurrencyHarness.BuildAsync(withActor: true);
        var (canonicalName, processName) = await StartProcessAsync(harness.Root);
        var baselineTransitions = await CountTransitionsAsync(harness.Root, processName);

        var outcome = await RunConcurrentCompletionsAsync(harness.Root, canonicalName, entry);

        Assert.Equal(0, outcome.ConflictCount);
        Assert.Empty(outcome.UnexpectedFailures);
        // Every concurrent Complete call reaches the actor's serialized turn and returns
        // successfully; only the first one to run finds the token still waiting and produces a
        // real transition. Every later turn reloads the process, finds the token already past
        // Doing, and returns the current snapshot unchanged (Transitions empty) — the state
        // machine's own idempotent-no-op behavior, not a race artifact.
        Assert.Equal(1, outcome.AdvancedCount);
        Assert.Equal(ConcurrentTaskCount - 1, outcome.NoOpCount);

        await using var verifyScope = harness.Root.CreateAsyncScope();
        var transitions = verifyScope.ServiceProvider.GetRequiredService<IRepository<SchemataProcessTransition>>();
        var rows = new List<SchemataProcessTransition>();
        await foreach (var row in transitions.ListAsync<SchemataProcessTransition>(
                           q => q.Where(t => t.Process == processName), CancellationToken.None)) {
            rows.Add(row);
        }

        // Start itself records one transition (the initial move into Doing); the concurrent
        // phase's own contribution is the count beyond that baseline, and must equal the single
        // real advance, with no duplicate rows for it.
        Assert.Equal(outcome.AdvancedCount, rows.Count - baselineTransitions);
        Assert.Equal(rows.Count, rows.Select(row => row.Uid).Distinct().Count());
    }

    private static async Task<int> CountTransitionsAsync(IServiceProvider root, string processName) {
        await using var scope       = root.CreateAsyncScope();
        var             transitions = scope.ServiceProvider.GetRequiredService<IRepository<SchemataProcessTransition>>();
        var             count       = 0;
        await foreach (var _ in transitions.ListAsync<SchemataProcessTransition>(
                           q => q.Where(t => t.Process == processName), CancellationToken.None)) {
            count++;
        }

        return count;
    }

    private static async Task<ConcurrencyOutcome> RunConcurrentCompletionsAsync(
        IServiceProvider root, string canonicalName, EntryPoint entry
    ) {
        // Barrier.SignalAndWait blocks its thread until every task has signaled. Without raising
        // the pool minimum, the CLR injects at most one new thread per second once the default
        // minimum is exhausted, which would starve the alignment itself and fail the 30-second
        // deadline on thread-pool scheduling latency rather than actor behavior.
        ThreadPool.GetMinThreads(out var minWorker, out var minIo);
        ThreadPool.SetMinThreads(Math.Max(minWorker, ConcurrentTaskCount + 4), minIo);

        using var barrier            = new Barrier(ConcurrentTaskCount);
        var       conflictCount      = 0;
        var       advancedCount      = 0;
        var       noOpCount          = 0;
        var       unexpectedFailures = new ConcurrentBag<Exception>();

        var tasks = Enumerable.Range(0, ConcurrentTaskCount).Select(_ => Task.Run(async () => {
            await using var scope = root.CreateAsyncScope();
            barrier.SignalAndWait();
            try {
                var snapshot = await CompleteOnceAsync(scope.ServiceProvider, canonicalName, entry);
                if (snapshot.Transitions.Count > 0) {
                    Interlocked.Increment(ref advancedCount);
                } else {
                    Interlocked.Increment(ref noOpCount);
                }
            } catch (AbortedException) {
                Interlocked.Increment(ref conflictCount);
            } catch (Exception ex) {
                unexpectedFailures.Add(ex);
            }
        })).ToArray();

        var all      = Task.WhenAll(tasks);
        var deadline = Task.Delay(CompletionDeadline);
        var finished = await Task.WhenAny(all, deadline);

        Assert.True(
            ReferenceEquals(finished, all),
            $"{ConcurrentTaskCount} concurrent completions did not finish within {CompletionDeadline}.");
        await all;

        return new(conflictCount, advancedCount, noOpCount, [.. unexpectedFailures]);
    }

    private static Task<ProcessSnapshot> CompleteOnceAsync(
        IServiceProvider services, string canonicalName, EntryPoint entry
    ) {
        return entry switch {
            EntryPoint.Facade => CompleteThroughFacadeAsync(services, canonicalName),
            EntryPoint.RequestDispatcher => services.GetRequiredService<IRequestDispatcher>()
                                                     .SendAsync<CompleteActivityRequest, ProcessSnapshot>(
                                                         new(canonicalName, null, null), CancellationToken.None),
            EntryPoint.CommandDispatcher => services.GetRequiredService<ICommandDispatcher>()
                                                     .SendAsync<CompleteActivityRequest, ProcessSnapshot>(
                                                         new(canonicalName, null, null), CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(entry)),
        };
    }

    private static async Task<ProcessSnapshot> CompleteThroughFacadeAsync(IServiceProvider services, string canonicalName) {
        var runner  = services.GetRequiredService<IFlowRunner>();
        var process = new SchemataProcess { CanonicalName = canonicalName };
        return await runner.CompleteAsync(process, null, (ClaimsPrincipal?)null, CancellationToken.None);
    }

    private static async Task<(string CanonicalName, string Name)> StartProcessAsync(IServiceProvider root) {
        await using var scope  = root.CreateAsyncScope();
        var             runner = scope.ServiceProvider.GetRequiredService<IFlowRunner>();
        var             process = await runner.StartAsync("ConcurrentActivityProcess");
        var canonicalName = process.CanonicalName ?? throw new InvalidOperationException("Started process has no canonical name.");
        var name          = process.Name ?? throw new InvalidOperationException("Started process has no name.");

        return (canonicalName, name);
    }

    private enum EntryPoint
    {
        Facade,
        RequestDispatcher,
        CommandDispatcher,
    }

    private sealed record ConcurrencyOutcome(
        int ConflictCount, int AdvancedCount, int NoOpCount, IReadOnlyList<Exception> UnexpectedFailures);
}
