using System.Threading;
using System.Threading.Tasks;
using Schemata.Entity.Owner;

namespace Schemata.Flow.Integration.Tests.Fixtures;

public sealed class AmbientOwnerResolver<TEntity> : IOwnerResolver<TEntity>
{
    public ValueTask<string?> ResolveAsync(CancellationToken ct) {
        return ValueTask.FromResult(AmbientOwner.Current.Value);
    }
}