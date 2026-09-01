using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Actor.Skeleton;

/// <summary>
///     Opts an actor into state persistence, only meaningful once the hosting capability enables
///     it. An actor that does not implement this interface, or a host that never enables the
///     capability, never has its <see cref="Entities.SchemataActor" /> row touched.
/// </summary>
public interface IPersistentActor : IActor
{
    /// <summary>
    ///     Called after every turn that completes without throwing, to capture the state to
    ///     persist.
    /// </summary>
    /// <param name="ctx">The context for the turn that just completed.</param>
    /// <returns>
    ///     The serialized state to upsert against this actor's <see cref="ActorId" />, or
    ///     <see langword="null" /> when the turn made no change worth persisting.
    /// </returns>
    ValueTask<byte[]?> SaveStateAsync(IActorContext ctx);

    /// <summary>
    ///     Called once, after the instance is constructed and before it receives its first
    ///     message, when a persisted state row already exists for this actor's <see cref="ActorId" />.
    /// </summary>
    /// <param name="ctx">The context for this actor instance.</param>
    /// <param name="state">The previously saved state, exactly as returned by <see cref="SaveStateAsync" />.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask LoadStateAsync(IActorContext ctx, byte[] state, CancellationToken ct = default);
}
