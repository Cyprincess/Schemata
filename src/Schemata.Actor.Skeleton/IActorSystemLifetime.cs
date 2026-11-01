using System;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Actor.Skeleton;

public interface IActorSystemLifetime : IAsyncDisposable
{
    Task BeginShutdown();
    Task ShutdownAsync(CancellationToken cancellationToken);
    Task CollectIdleAsync(CancellationToken cancellationToken);
}
