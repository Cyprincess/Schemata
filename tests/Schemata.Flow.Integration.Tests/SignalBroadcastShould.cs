using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Entity.Repository;
using Schemata.Flow.Foundation;
using Schemata.Flow.Integration.Tests.Fixtures;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;

namespace Schemata.Flow.Integration.Tests;

[Trait("Category", "Integration")]
public class SignalBroadcastShould(EfCoreFlowFixture fixture) : IClassFixture<EfCoreFlowFixture>
{
    private const string SignalName = "broadcast-signal";

    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(50);

    [Fact]
    public async Task ReportOneFailedTarget_AndStillCommitTheOthers() {
        fixture.CatchKinds.Add(FlowCatchKind.Signal);
        var concurrency = fixture.FlowOptions.SignalBroadcastConcurrency;
        fixture.FlowOptions.SignalBroadcastConcurrency = 1;

        var processes = await StartWaitingProcessesAsync(3);

        // Results come back ordered by canonical name, so the failure is armed on whichever process
        // sorts into the middle.
        var ordered = processes.OrderBy(p => p.CanonicalName, StringComparer.Ordinal).ToList();
        var failing = ordered[1].CanonicalName!;
        fixture.FailingProcesses.Add(failing);

        try {
            var results = await BroadcastAsync();

            Assert.Equal(3, results.Count);
            Assert.Equal(
                [SignalDeliveryStatus.Delivered, SignalDeliveryStatus.Failed, SignalDeliveryStatus.Delivered],
                results.Select(r => r.Status));
            Assert.Equal(ordered.Select(p => p.CanonicalName), results.Select(r => r.ProcessCanonicalName));
            Assert.NotNull(results[1].Error);

            // The siblings' transitions are committed, and the failed one rolled back to its catch.
            Assert.Null(await ReadWaitingAtAsync(ordered[0].Name!));
            Assert.Equal("signal-catch", await ReadWaitingAtAsync(ordered[1].Name!));
            Assert.Null(await ReadWaitingAtAsync(ordered[2].Name!));
        } finally {
            fixture.FailingProcesses.Remove(failing);
            fixture.FlowOptions.SignalBroadcastConcurrency = concurrency;
        }
    }

    [Fact]
    public async Task RunDeliveriesSerially_WhenConcurrencyIsOne() {
        fixture.CatchKinds.Add(FlowCatchKind.Signal);
        var concurrency = fixture.FlowOptions.SignalBroadcastConcurrency;
        fixture.FlowOptions.SignalBroadcastConcurrency = 1;
        fixture.TransitionDelay = Delay;
        fixture.ResetCounters();

        try {
            await StartWaitingProcessesAsync(3);

            var started = Stopwatch.GetTimestamp();
            var results = await BroadcastAsync();
            var elapsed = Stopwatch.GetElapsedTime(started);

            Assert.All(results, result => Assert.Equal(SignalDeliveryStatus.Delivered, result.Status));

            // A peak of one only measures the bound if the deliveries could have raced: the counter
            // must have seen every target, and the wall clock must show the delays ran end to end
            // rather than side by side.
            Assert.True(fixture.TransitionCount >= 3, $"armed {fixture.TransitionCount} transitions");
            Assert.True(elapsed >= 3 * Delay, $"broadcast took {elapsed.TotalMilliseconds:F0}ms");
            Assert.Equal(1, fixture.PeakConcurrency);
        } finally {
            fixture.TransitionDelay = TimeSpan.Zero;
            fixture.FlowOptions.SignalBroadcastConcurrency = concurrency;
        }
    }

    private async Task<IReadOnlyList<SchemataProcess>> StartWaitingProcessesAsync(int count) {
        var definition = $"{nameof(SignalBroadcastProcess)}-{Guid.NewGuid():n}";

        using (var scope = fixture.CreateScope()) {
            var registry = scope.ServiceProvider.GetRequiredService<IProcessRegistry>();
            await registry.RegisterAsync(new ProcessConfiguration {
                Name           = definition,
                Engine         = FlowConstants.Engines.Bpmn,
                DefinitionType = typeof(SignalBroadcastProcess),
            });
        }

        var started = new List<SchemataProcess>(count);
        for (var i = 0; i < count; i++) {
            using var scope  = fixture.CreateScope();
            var       runner = scope.ServiceProvider.GetRequiredService<FlowRunner>();
            started.Add(await runner.StartAsync(definition, null, CancellationToken.None));
        }

        return started;
    }

    private async Task<IReadOnlyList<SignalDeliveryResult>> BroadcastAsync() {
        using var scope  = fixture.CreateScope();
        var       runner = scope.ServiceProvider.GetRequiredService<FlowRunner>();
        return await runner.ThrowSignalAsync(SignalName, (string?)null, null, null, CancellationToken.None);
    }

    private async Task<string?> ReadWaitingAtAsync(string processName) {
        using var scope      = fixture.CreateScope();
        var       repository = scope.ServiceProvider.GetRequiredService<IRepository<SchemataProcessToken>>();
        var token = await repository.FirstOrDefaultAsync(q => q.Where(t => t.Process == processName));
        Assert.NotNull(token);
        return token!.WaitingAtName;
    }
}

public sealed class SignalBroadcastProcess : ProcessDefinition
{
    public SignalBroadcastProcess() {
        var start  = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var signal = new Signal { Name = "broadcast-signal" };
        var catchEvent = new FlowEvent {
            Name       = "signal-catch",
            Position   = EventPosition.IntermediateCatch,
            Definition = signal,
        };
        var end = new FlowEvent { Name = "end", Position = EventPosition.End };

        Elements.AddRange([start, catchEvent, end]);
        Signals.Add(signal);
        Flows.Add(new() { Source = start, Target = catchEvent });
        Flows.Add(new() { Source = catchEvent, Target = end });
    }
}
