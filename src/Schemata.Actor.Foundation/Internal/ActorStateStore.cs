using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Actor.Skeleton;
using Schemata.Actor.Skeleton.Entities;
using Schemata.Entity.Repository;

namespace Schemata.Actor.Foundation.Internal;

/// <summary>
///     Reads and writes the opaque <see cref="SchemataActor.State" /> row for an
///     <see cref="IPersistentActor" />, keyed by <see cref="ActorId.ToString" />. Registered only
///     by <see cref="SchemataActorBuilder.UsePersistence" /> - the sole channel that adds this
///     type to the container (R8). Its constructor resolves
///     <see cref="IRepository{TEntity}" /> directly, so dependency injection raises the resolution
///     failure itself when an application enables persistence without registering
///     <c>IRepository&lt;SchemataActor&gt;</c>.
/// </summary>
internal sealed class ActorStateStore(IRepository<SchemataActor> repository)
{
    /// <summary>Reads the persisted state for <paramref name="id" />, or <see langword="null" /> when no row exists yet.</summary>
    /// <param name="id">The owning actor's identity.</param>
    /// <param name="ct">A cancellation token.</param>
    public async ValueTask<byte[]?> LoadAsync(ActorId id, CancellationToken ct) {
        var row = await FindAsync(id, ct);

        return row?.State;
    }

    /// <summary>Upserts <paramref name="state" /> as the persisted row for <paramref name="id" />.</summary>
    /// <param name="id">The owning actor's identity.</param>
    /// <param name="state">The state produced by <see cref="IPersistentActor.SaveStateAsync" />.</param>
    /// <param name="ct">A cancellation token.</param>
    public async Task SaveAsync(ActorId id, byte[] state, CancellationToken ct) {
        var existing = await FindAsync(id, ct);
        if (existing is null) {
            await repository.AddAsync(new() { Name = id.ToString(), State = state }, ct);
        } else {
            existing.State = state;
            await repository.UpdateAsync(existing, ct);
        }

        await repository.CommitAsync(ct);
    }

    private ValueTask<SchemataActor?> FindAsync(ActorId id, CancellationToken ct) {
        var name = id.ToString();

        return repository.FirstOrDefaultAsync<SchemataActor>(q => q.Where(actor => actor.Name == name), ct);
    }
}
