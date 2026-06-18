using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Scheduling.Foundation.Internal;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Tests;

public class OperationDescriptorShould
{
    [Fact]
    public async Task RebuiltFromDescriptor_AfterRestart() {
        // A restarted process: a fresh container with only the registry + keyed
        // handler (no in-process closure), and an execution row reloaded from
        // persistence carrying the operation key and serialized arguments.
        var handler = new RecordingHandler();

        var services = new ServiceCollection();
        services.AddSingleton<IOperationRegistry>(new StubRegistry(
            new("sample:op", "sample", typeof(SampleArgs))));
        services.AddKeyedSingleton<IOperationHandler<SampleArgs>>("sample:op", handler);
        var provider = services.BuildServiceProvider();

        var execution = new SchemataJobExecution {
            Uid               = Identifiers.NewUid(),
            JobKey            = "sample:op",
            ArgsJson          = JsonSerializer.Serialize(new SampleArgs { Value = "v" }, SchemataJson.Default),
            State             = ExecutionState.Pending,
        };

        var job = new DurableOperationScheduledJob<SampleArgs>(provider);
        await job.ExecuteAsync(new() { Job = "operations/x", Execution = execution }, CancellationToken.None);

        Assert.Equal("v", handler.Received?.Value);
        Assert.Equal("{\"Value\":\"done\"}", execution.Output);
    }

    [Fact]
    public async Task MissingDescriptor_FailsFast() {
        var provider  = new ServiceCollection().BuildServiceProvider();
        var job       = new DurableOperationScheduledJob<SampleArgs>(provider);
        var execution = new SchemataJobExecution { Uid = Identifiers.NewUid() };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            job.ExecuteAsync(new() { Job = "operations/x", Execution = execution }, CancellationToken.None));
    }

    private sealed class SampleArgs
    {
        public string? Value { get; set; }
    }

    private sealed class RecordingHandler : IOperationHandler<SampleArgs>
    {
        public SampleArgs? Received { get; private set; }

        public Task<object?> RunAsync(SampleArgs args, CancellationToken ct) {
            Received = args;
            return Task.FromResult<object?>(new SampleArgs { Value = "done" });
        }
    }

    private sealed class StubRegistry(OperationDescriptor descriptor) : IOperationRegistry
    {
        public OperationDescriptor GetRequired(string key) {
            return key == descriptor.Key
                ? descriptor
                : throw new InvalidOperationException($"No operation '{key}'.");
        }
    }
}
