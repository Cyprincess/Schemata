using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Actor.Skeleton;

namespace Schemata.Actor.Foundation;

/// <summary>
///     Resolves the per-instance actor that should serialize one request, or <see langword="null" />
///     when the request has no instance identity and must use the keyed default handler.
/// </summary>
/// <typeparam name="TRequest">The routed request type.</typeparam>
public interface IActorRouting<in TRequest>
{
    /// <summary>Resolves the target actor for <paramref name="request" />.</summary>
    ValueTask<ActorId?> ResolveAsync(
        TRequest         request,
        IServiceProvider services,
        CancellationToken ct);
}
