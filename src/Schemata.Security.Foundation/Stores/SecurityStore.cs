using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;

namespace Schemata.Security.Foundation.Stores;

/// <summary>
///     Default implementation of <see cref="ISecurityStore{TSecurity}" /> backed by an
///     <see cref="IRepository{TEntity}" /> for reads and the injected
///     <see cref="IResourceMutation{TSecurity}" /> owner for writes. Rows are stored verbatim, in
///     plaintext at rest; transparent at-rest encryption is a documented non-goal. A
///     <c>beforeCommit</c> participant runs inside one caller-owned transaction together with the
///     staged write.
/// </summary>
/// <typeparam name="TSecurity">Concrete security entity type, must derive from <see cref="SchemataSecurity" />.</typeparam>
public class SecurityStore<TSecurity> : ISecurityStore<TSecurity>
    where TSecurity : SchemataSecurity, new()
{
    private readonly IResourceMutation<TSecurity> _mutation;
    private readonly IServiceProvider             _services;

    public SecurityStore(
        IResourceMutation<TSecurity> mutation,
        IServiceProvider             services
    ) {
        _mutation   = mutation;
        _services   = services;
    }

    #region ISecurityStore<TSecurity> Members

    public async Task<TSecurity?> FindByCanonicalNameAsync(string? canonicalName, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(canonicalName)) {
            return null;
        }

        await using var repository = _services.GetRequiredService<IRepository<TSecurity>>();
        return await repository.SingleOrDefaultAsync(q => q.Where(s => s.CanonicalName == canonicalName), ct);
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

        await using var repository = _services.GetRequiredService<IRepository<TSecurity>>();
        await foreach (var security in repository.ListAsync(q => {
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

            return query.OrderByDescending(s => s.CreateTime).ThenBy(s => s.Key);
        }, ct)) {
            yield return security;
        }
    }

    public async Task<TSecurity?> CreateAsync(TSecurity? security, CancellationToken ct = default,
        Func<IUnitOfWork, CancellationToken, Task>? beforeCommit = null) {
        ct.ThrowIfCancellationRequested();

        if (security is null) {
            return null;
        }

        if (beforeCommit is null) {
            await _mutation.CreateAsync(security, null, ct);
            return security;
        }

        await using var repository = _services.GetRequiredService<IRepository<TSecurity>>();
        await using var transaction = repository.Begin();
        await _mutation.CreateAsync(security, transaction, ct);
        await beforeCommit(transaction, ct);
        await transaction.CommitAsync(ct);

        return security;
    }

    public async Task UpdateAsync(TSecurity? security, CancellationToken ct = default,
        Func<IUnitOfWork, CancellationToken, Task>? beforeCommit = null) {
        ct.ThrowIfCancellationRequested();

        if (security is null) {
            return;
        }

        if (beforeCommit is null) {
            await _mutation.UpdateAsync(security, null, Operations.Update, ct);
            return;
        }

        await using var repository = _services.GetRequiredService<IRepository<TSecurity>>();
        await using var transaction = repository.Begin();
        await _mutation.UpdateAsync(security, transaction, Operations.Update, ct);
        await beforeCommit(transaction, ct);
        await transaction.CommitAsync(ct);
    }

    public async Task DeleteAsync(TSecurity? security, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();

        if (security is null) {
            return;
        }

        await _mutation.DeleteAsync(security, null, Operations.Delete, ct);
    }

    #endregion
}
