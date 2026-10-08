using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Actor.Skeleton;
using Schemata.Actor.Skeleton.Entities;
using Schemata.Entity.Repository;

namespace Schemata.Actor.Foundation.Runtime;

/// <summary>
///     Reads and writes the opaque <see cref="SchemataActor.State" /> row for an
///     <see cref="IPersistentActor" />, partitioned by tenant, type and instance key.
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
            await repository.AddAsync(new() { Tenant = id.Tenant.Uid.HasValue ? id.Tenant.Uid.Value.ToString("D") : "host", ActorType = id.Type, ActorKey = id.Key, State = state }, ct);
        } else {
            existing.State = state;
            await repository.UpdateAsync(existing, ct);
        }

        await repository.CommitAsync(ct);
    }

    private ValueTask<SchemataActor?> FindAsync(ActorId id, CancellationToken ct) {
        var tenant = id.Tenant.Uid?.ToString("D") ?? "host";
        return repository.FirstOrDefaultAsync<SchemataActor>(
            q => q.Where(actor => actor.Tenant == tenant && actor.ActorType == id.Type && actor.ActorKey == id.Key), ct);
    }
}
