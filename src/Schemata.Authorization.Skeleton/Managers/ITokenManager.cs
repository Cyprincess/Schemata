using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Entities;

namespace Schemata.Authorization.Skeleton.Managers;

public interface ITokenManager<TToken>
    where TToken : SchemataToken
{
    Task<TToken?> FindByReferenceIdAsync(string? reference, CancellationToken ct = default);

    Task<TToken?> FindByCanonicalNameAsync(string? name, CancellationToken ct = default);

    IAsyncEnumerable<TToken> ListBySessionAsync(string? session, CancellationToken ct = default);

    IAsyncEnumerable<TToken> ListBySubjectAsync(string? subject, CancellationToken ct = default);

    Task<TToken?> CreateAsync(TToken? token, CancellationToken ct = default);

    Task UpdateAsync(TToken? token, CancellationToken ct = default);

    Task RevokeAsync(TToken? token, CancellationToken ct = default);

    Task<long> RevokeByAuthorizationAsync(string? authorization, CancellationToken ct = default);

    Task<long> PruneAsync(DateTime? threshold, CancellationToken ct = default);
}
