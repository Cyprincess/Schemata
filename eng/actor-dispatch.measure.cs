#:project ../tests/Schemata.Actor.Foundation.Tests/Schemata.Actor.Foundation.Tests.csproj

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Actor.Foundation.Runtime;
using Schemata.Actor.Foundation.Tests.Fixtures;
using Schemata.Abstractions.Advisors;
using Schemata.Messaging.Skeleton;

const int warmupCalls = 500;
const int callsPerBatch = 5_000;

var (system, registry, root) = ActorSystemFactory.Create(services => {
    services.AddKeyedScoped<IRequestHandler<MeasureRequest, long>>("measure", (_, _) => new MeasureHandler());
});
registry.Register("measure", new(typeof(RequestDispatchingActor), ["measure"]));

try {
    var actor   = await system.GetAsync(new("measure", "single"));
    var request = new MeasureRequest(1);

    await actor.TellAsync(root, request);
    var expected = await actor.AskAsync<MeasureRequest, long>(root, request, timeout: TimeSpan.FromSeconds(30));
    for (var i = 0; i < warmupCalls; i++) {
        expected = await actor.AskAsync<MeasureRequest, long>(root, request, timeout: TimeSpan.FromSeconds(30));
    }

    var batches = new List<MeasureBatch>(5);
    for (var batch = 0; batch < 5; batch++) {
        var allocated = GC.GetTotalAllocatedBytes(true);
        var started   = Stopwatch.GetTimestamp();
        for (var i = 0; i < callsPerBatch; i++) {
            expected = await actor.AskAsync<MeasureRequest, long>(root, request, timeout: TimeSpan.FromSeconds(30));
        }
        var elapsed = Stopwatch.GetElapsedTime(started);
        var bytes   = GC.GetTotalAllocatedBytes(true) - allocated;
        batches.Add(new(elapsed.TotalMilliseconds, callsPerBatch / elapsed.TotalSeconds, bytes, (double)bytes / callsPerBatch));
    }

    if (expected != request.Amount) {
        throw new InvalidOperationException($"Unexpected final response: {expected}.");
    }

    var measurement = new ActorDispatchMeasurement(
        new(RuntimeInformation.OSDescription, RuntimeInformation.OSArchitecture.ToString(), RuntimeInformation.ProcessArchitecture.ToString(), Environment.ProcessorCount),
        RuntimeInformation.FrameworkDescription,
        warmupCalls,
        callsPerBatch,
        batches);
    Console.WriteLine(JsonSerializer.Serialize(measurement, MeasurementJsonContext.Default.ActorDispatchMeasurement));
} finally {
    await system.ShutdownAsync(default);
    if (root is IAsyncDisposable disposable) {
        await disposable.DisposeAsync();
    }
}

internal sealed record MeasureRequest(int Amount) : IRequest<long>;

internal sealed class MeasureHandler : IRequestHandler<MeasureRequest, long>
{
    public Task<long> HandleAsync(MeasureRequest request, AdviceContext context, CancellationToken ct = default) =>
        Task.FromResult((long)request.Amount);
}

internal sealed record MeasureEnvironment(string Os, string OsArchitecture, string ProcessArchitecture, int ProcessorCount);

internal sealed record MeasureBatch(double ElapsedMilliseconds, double ThroughputCallsPerSecond, long TotalAllocatedBytes, double BytesPerCall);

internal sealed record ActorDispatchMeasurement(
    MeasureEnvironment Environment,
    string Framework,
    int WarmupCalls,
    int CallsPerBatch,
    IReadOnlyList<MeasureBatch> Batches);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, WriteIndented = true)]
[JsonSerializable(typeof(ActorDispatchMeasurement))]
internal sealed partial class MeasurementJsonContext : JsonSerializerContext;
