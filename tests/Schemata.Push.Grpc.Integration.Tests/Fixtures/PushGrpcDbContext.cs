using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Schemata.Push.Skeleton;
using Schemata.Push.Skeleton.Entities;

namespace Schemata.Push.Grpc.Integration.Tests.Fixtures;

public class PushGrpcDbContext(DbContextOptions<PushGrpcDbContext> options) : DbContext(options)
{
    public DbSet<SchemataPushSubscription> Subscriptions { get; set; } = null!;
}

public sealed class RecordingTransport(string name) : IPushTransport
{
    public string Name { get; } = name;
    public ConcurrentQueue<PushContext> Deliveries { get; } = new();

    public ValueTask<TransportResult> TrySendAsync(PushContext context, CancellationToken ct = default) {
        Deliveries.Enqueue(context);
        return ValueTask.FromResult(Name switch {
            "ok"    => TransportResult.Sent("ok", address: "masked"),
            "later" => TransportResult.Failed("later", "backend unavailable"),
            _       => TransportResult.Skipped(Name),
        });
    }
}
