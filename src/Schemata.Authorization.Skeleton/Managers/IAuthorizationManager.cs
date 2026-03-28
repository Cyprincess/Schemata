using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Entities;

namespace Schemata.Authorization.Skeleton.Managers;

public interface IAuthorizationManager<TAuthorization>
    where TAuthorization : SchemataAuthorization
{
    IAsyncEnumerable<TAuthorization> ListAsync(string? subject, string? client, CancellationToken ct = default);

    Task<TAuthorization?> FindByCanonicalNameAsync(string? name, CancellationToken ct = default);

    Task<TAuthorization?> CreateAsync(TAuthorization? authorization, CancellationToken ct = default);

    Task RevokeAsync(TAuthorization? authorization, CancellationToken ct = default);

    Task UpdateAsync(TAuthorization? authorization, CancellationToken ct = default);

    Task DeleteAsync(TAuthorization? authorization, CancellationToken ct = default);
}
