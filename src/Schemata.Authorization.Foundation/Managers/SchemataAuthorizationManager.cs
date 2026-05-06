using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Entity.Repository;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Managers;

/// <summary>
///     Default implementation of <see cref="IAuthorizationManager{TAuthorization}" /> backed by an
///     <see cref="IRepository{TEntity}" />.
/// </summary>
/// <typeparam name="TAuthorization">The authorization entity type, must derive from <see cref="SchemataAuthorization" />.</typeparam>
/// <remarks>
///     Authorizations represent a user's consent to a specific application for a set of scopes. They are
///     keyed by subject + application name. Revocation sets the status to <see cref="TokenStatuses.Revoked" />
///     without physically deleting the record.
/// </remarks>
/// <seealso cref="SchemataTokenManager{TToken}" />
public class SchemataAuthorizationManager<TAuthorization> : IAuthorizationManager<TAuthorization>
    where TAuthorization : SchemataAuthorization
{
    private readonly IRepository<TAuthorization> _authorizations;

    public SchemataAuthorizationManager(IRepository<TAuthorization> authorizations) {
        _authorizations = authorizations;
    }

    #region IAuthorizationManager<TAuthorization> Members

    public async IAsyncEnumerable<TAuthorization> ListAsync(
        string?                                    subject,
        string?                                    client,
        [EnumeratorCancellation] CancellationToken ct = default
    ) {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(subject)) {
            yield break;
        }

        if (string.IsNullOrWhiteSpace(client)) {
            yield break;
        }

        await foreach (var authorization in _authorizations.ListAsync(
                           q => q.Where(a => a.Subject == subject && a.ApplicationName == client), ct)) {
            yield return authorization;
        }
    }

    public async Task<TAuthorization?> CreateAsync(TAuthorization? authorization, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (authorization is null) {
            return null;
        }

        await _authorizations.AddAsync(authorization, ct);
        await _authorizations.CommitAsync(ct);

        return authorization;
    }

    public async Task RevokeAsync(TAuthorization? authorization, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (authorization is null) {
            return;
        }

        authorization.Status = TokenStatuses.Revoked;

        await _authorizations.UpdateAsync(authorization, ct);
        await _authorizations.CommitAsync(ct);
    }

    public async Task UpdateAsync(TAuthorization? authorization, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (authorization is null) {
            return;
        }

        await _authorizations.UpdateAsync(authorization, ct);
        await _authorizations.CommitAsync(ct);
    }

    public async Task DeleteAsync(TAuthorization? authorization, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (authorization is null) {
            return;
        }

        await _authorizations.RemoveAsync(authorization, ct);
        await _authorizations.CommitAsync(ct);
    }

    #endregion
}
