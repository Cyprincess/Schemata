using System;
using System.Threading.Tasks;
using Schemata.Actor.Foundation.Runtime;
using Schemata.Actor.Foundation.Tests.Fixtures;
using Xunit;

namespace Schemata.Actor.Foundation.Tests;

/// <summary>
///     Barrier-controlled coverage for activation eligibility and retirement transitions, per
///     issue #132: a graceful stop arriving before initialization finishes still completes
///     initialization and drains accepted work; a startup failure overrides earlier graceful
///     intent and faults queued work without normal receives; late sends fail explicitly.
/// </summary>
public class ActorActivationShould
{
    private static TaskCompletionSource<bool> ReleaseSource() {
        return new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    [Fact]
    public async Task Graceful_Stop_Before_Start_Completes_Initialization_And_Drains_Accepted_Work() {
        var (system, _, _) = ActorSystemFactory.Create();
        var recorder       = new LifecycleRecorder();
        var startReleased  = ReleaseSource();
        var actor          = await system.SpawnAsync(
            new("start-gated", "a"),
            new(typeof(StartGateActor), [recorder, startReleased]));

        // AskAsync writes synchronously into the bounded channel: by the time this call returns
        // the item is accepted work.
        var accepted = actor.AskAsync<AskEcho, string>(new()).AsTask();

        // The graceful stop signal runs synchronously through the state transition and the
        // writer completion before StopAsync first suspends.
        var stopping = system.StopAsync(new("start-gated", "a"));

        // Initialization is still blocked; release it only now.
        startReleased.SetResult(true);

        Assert.Equal("received", await accepted.WaitAsync(TimeSpan.FromSeconds(10)));
        await stopping.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(new[] { "OnStarted", "OnReceive", "OnStopped" }, recorder.Events);
    }

    [Fact]
    public async Task Startup_Failure_After_Graceful_Intent_Faults_Queued_Work_Without_Receives() {
        var (system, _, _) = ActorSystemFactory.Create();
        var recorder       = new LifecycleRecorder();
        var startReleased  = ReleaseSource();
        var actor          = await system.SpawnAsync(
            new("failing-start", "a"),
            new(typeof(FailingStartActor), [recorder, startReleased]));

        var accepted = actor.AskAsync<AskEcho, string>(new()).AsTask();
        var stopping = system.StopAsync(new("failing-start", "a"));

        startReleased.SetResult(true);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => accepted.WaitAsync(TimeSpan.FromSeconds(10)));
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => stopping.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal("startup failed", failure.Message);

        Assert.Equal(new[] { "OnStarted", "OnStopped" }, recorder.Events);
    }

    [Fact]
    public async Task Retained_Reference_Delivers_After_Explicit_Retirement() {
        var (system, _, _) = ActorSystemFactory.Create();
        var recorder       = new LifecycleRecorder();
        var startReleased  = ReleaseSource();
        startReleased.SetResult(true);
        var actor = await system.SpawnAsync(
            new("retiring", "a"),
            new(typeof(StartGateActor), [recorder, startReleased]));

        await system.StopAsync(new("retiring", "a"));

        await actor.TellAsync(new AskEcho());
        Assert.Equal("received", await actor.AskAsync<AskEcho, string>(new()));
        await system.StopAsync(actor.Id);
    }

    [Fact]
    public async Task Startup_Failure_Without_Graceful_Intent_Faults_Queued_Work() {
        var (system, _, _) = ActorSystemFactory.Create();
        var recorder       = new LifecycleRecorder();
        var startReleased  = ReleaseSource();
        var actor          = await system.SpawnAsync(
            new("failing-start-2", "a"),
            new(typeof(FailingStartActor), [recorder, startReleased]));

        var accepted = actor.AskAsync<AskEcho, string>(new()).AsTask();

        startReleased.SetResult(true);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => accepted.WaitAsync(TimeSpan.FromSeconds(10)));
    }
}
