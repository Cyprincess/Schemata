using System;
using Moq;
using Schemata.Entity.Repository;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;

namespace Schemata.Authorization.Tests;

/// <summary>
///     In-memory <see cref="ISecurityStore{TSecurity}" /> standing in for the host store in
///     unit tests. Rows order by create time descending then key ascending, mirroring the
///     store contract the token pipeline relies on for primary-key selection.
/// </summary>
public class TestSecurityStore : ISecurityStore<SchemataSecurity>
{
    private readonly List<SchemataSecurity> _rows = [];

    /// <summary>Number of <see cref="ListByParentAsync" /> invocations observed; proves an
    /// operation resolves its signing selection once instead of re-querying per token.</summary>
    public int ListByParentCalls { get; private set; }

    /// <summary>Number of rows currently held.</summary>
    public int Count => _rows.Count;

    /// <inheritdoc />
    public Task<SchemataSecurity?> FindByCanonicalNameAsync(string? canonicalName, CancellationToken ct = default) {
        return Task.FromResult(_rows.FirstOrDefault(row => row.CanonicalName == canonicalName));
    }

    /// <inheritdoc />
    public IAsyncEnumerable<SchemataSecurity> ListByParentAsync(
        string?           parent,
        string?           kind   = null,
        string?           usage  = null,
        string?           status = null,
        CancellationToken ct     = default
    ) {
        ListByParentCalls++;
        return Enumerate(_rows
            .Where(row => row.Parent == parent
                       && (kind is null || row.Kind == kind)
                       && (usage is null || row.Usage == usage)
                       && (status is null || row.Status == status))
            .OrderByDescending(row => row.CreateTime)
            .ThenBy(row => row.Key));
    }

    /// <inheritdoc />
    public async Task<SchemataSecurity?> CreateAsync(SchemataSecurity? security, CancellationToken ct = default,
        Func<IUnitOfWork, CancellationToken, Task>? beforeCommit = null) {
        if (security is null) {
            return null;
        }

        if (beforeCommit is not null) await beforeCommit(new Mock<IUnitOfWork>().Object, ct);
        _rows.Add(security);
        return security;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(SchemataSecurity? security, CancellationToken ct = default,
        Func<IUnitOfWork, CancellationToken, Task>? beforeCommit = null) {
        if (beforeCommit is not null) await beforeCommit(new Mock<IUnitOfWork>().Object, ct);
    }

    /// <inheritdoc />
    public Task DeleteAsync(SchemataSecurity? security, CancellationToken ct = default) {
        if (security is not null) {
            _rows.Remove(security);
        }

        return Task.CompletedTask;
    }

    private async IAsyncEnumerable<SchemataSecurity> Enumerate(
        IEnumerable<SchemataSecurity> rows,
        [EnumeratorCancellation] CancellationToken ct = default
    ) {
        foreach (var row in rows) {
            await Task.Yield();
            yield return row;
        }
    }
}
