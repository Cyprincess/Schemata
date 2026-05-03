using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Entities;

namespace Schemata.Authorization.Skeleton.Managers;

/// <summary>
///     Manages <see cref="SchemataAuthorization" /> consent records.
/// </summary>
public interface IAuthorizationManager<TAuthorization>
    where TAuthorization : SchemataAuthorization
{
    /// <summary>Lists authorizations for a given subject and/or client.</summary>
    IAsyncEnumerable<TAuthorization> ListAsync(string? subject, string? client, CancellationToken ct = default);

    /// <summary>Finds an authorization by its canonical name.</summary>
    Task<TAuthorization?> FindByCanonicalNameAsync(string? name, CancellationToken ct = default);

    /// <summary>Creates a new authorization consent record.</summary>
    Task<TAuthorization?> CreateAsync(TAuthorization? authorization, CancellationToken ct = default);

    /// <summary>Revokes an authorization, invalidating all derived tokens.</summary>
    Task RevokeAsync(TAuthorization? authorization, CancellationToken ct = default);

    /// <summary>Updates an existing authorization record.</summary>
    Task UpdateAsync(TAuthorization? authorization, CancellationToken ct = default);

    /// <summary>Deletes an authorization record.</summary>
    Task DeleteAsync(TAuthorization? authorization, CancellationToken ct = default);
}
