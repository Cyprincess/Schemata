using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Entity.Repository;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;

namespace Schemata.Security.Foundation.Stores;

/// <summary>
///     Default implementation of <see cref="ISecurityStore{TSecurity}" /> backed by an
///     <see cref="IRepository{TEntity}" />. Rows are stored verbatim, in plaintext at rest;
///     transparent at-rest encryption is a documented non-goal.
/// </summary>
/// <typeparam name="TSecurity">Concrete security entity type, must derive from <see cref="SchemataSecurity" />.</typeparam>
public class SecurityStore<TSecurity> : ISecurityStore<TSecurity>
    where TSecurity : SchemataSecurity, new()
{
    private readonly IRepository<TSecurity> _repository;

    public SecurityStore(IRepository<TSecurity> repository) {
        _repository = repository;
    }

    #region ISecurityStore<TSecurity> Members

    public async Task<TSecurity?> FindByCanonicalNameAsync(string? canonicalName, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(canonicalName)) {
            return null;
        }

        return await _repository.SingleOrDefaultAsync(q => q.Where(s => s.CanonicalName == canonicalName), ct);
    }

    public async IAsyncEnumerable<TSecurity> ListByParentAsync(
        string?                                    parent,
        string?                                    kind     = null,
        string?                                    usage    = null,
        string?                                    status   = null,
        [EnumeratorCancellation] CancellationToken ct       = default
    ) {
        if (string.IsNullOrWhiteSpace(parent)) {
            yield break;
        }

        await foreach (var security in _repository.ListAsync(q => {
            var query = q.Where(s => s.Parent == parent);
            if (kind is not null) {
                query = query.Where(s => s.Kind == kind);
            }

            if (usage is not null) {
                query = query.Where(s => s.Usage == usage);
            }

            if (status is not null) {
                query = query.Where(s => s.Status == status);
            }

            return query.OrderByDescending(s => s.CreateTime).ThenBy(s => s.Name);
        }, ct)) {
            yield return security;
        }
    }

    public async Task<TSecurity?> CreateAsync(TSecurity? security, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (security is null) {
            return null;
        }

        await _repository.AddAsync(security, ct);
        await _repository.CommitAsync(ct);

        return security;
    }

    public async Task UpdateAsync(TSecurity? security, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (security is null) {
            return;
        }

        await _repository.UpdateAsync(security, ct);
        await _repository.CommitAsync(ct);
    }

    public async Task DeleteAsync(TSecurity? security, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (security is null) {
            return;
        }

        await _repository.RemoveAsync(security, ct);
        await _repository.CommitAsync(ct);
    }

    #endregion
}
