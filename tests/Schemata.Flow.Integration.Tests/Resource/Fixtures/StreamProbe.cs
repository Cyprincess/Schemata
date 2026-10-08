using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Exceptions;
using Schemata.Messaging.Skeleton;

namespace Schemata.Flow.Integration.Tests.Resource.Fixtures;

public sealed class StreamProbeRequest : IStreamRequest<StreamProbeItem>
{
    public string Id { get; set; } = string.Empty;
    public bool FailBefore { get; set; }
    public bool FailAfter { get; set; }
}
public sealed class StreamProbeItem { public int Value { get; set; } }
public sealed class StreamProbeState
{
    public ConcurrentDictionary<string, TaskCompletionSource> Release { get; } = new();
    public ConcurrentDictionary<string, TaskCompletionSource> Disposed { get; } = new();
    public TaskCompletionSource Gate(string id) => Release.GetOrAdd(id, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
    public TaskCompletionSource End(string id) => Disposed.GetOrAdd(id, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
    public ConcurrentDictionary<string, int> ScopeDisposals { get; } = new();
    public ConcurrentDictionary<string, TaskCompletionSource> ScopeEnded { get; } = new();
    public TaskCompletionSource ScopeEnd(string id) => ScopeEnded.GetOrAdd(id, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
}

public sealed class StreamScopeProbe(StreamProbeState state) : IAsyncDisposable
{
    public string? Id { get; set; }
    public ValueTask DisposeAsync() {
        if (Id is { } id) {
            state.ScopeDisposals.AddOrUpdate(id, 1, (_, count) => count + 1);
            state.ScopeEnd(id).TrySetResult();
        }
        return ValueTask.CompletedTask;
    }
}
public sealed class StreamProbeHandler(StreamProbeState state, StreamScopeProbe scope) : IStreamRequestHandler<StreamProbeRequest, StreamProbeItem>
{
    public async IAsyncEnumerable<StreamProbeItem> HandleAsync(StreamProbeRequest request, StreamExecutionContext context, [EnumeratorCancellation] CancellationToken ct = default) {
        scope.Id = request.Id;
        try {
            if (request.FailBefore) throw new InvalidArgumentException("Rejected before output.");
            yield return new() { Value = 1 };
            await state.Gate(request.Id).Task.WaitAsync(ct);
            if (request.FailAfter) throw new InvalidArgumentException("Rejected after output.");
            yield return new() { Value = 2 };
        } finally { state.End(request.Id).TrySetResult(); }
    }
}
