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
///     unit tests. Rows order by create time descending then name ascending, mirroring the
///     store contract the token pipeline relies on for primary-key selection.
/// </summary>
public class TestSecurityStore : ISecurityStore<SchemataSecurity>
{
    private readonly List<SchemataSecurity> _rows = [];

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
        return Enumerate(_rows
            .Where(row => row.Parent == parent
                       && (kind is null || row.Kind == kind)
                       && (usage is null || row.Usage == usage)
                       && (status is null || row.Status == status))
            .OrderByDescending(row => row.CreateTime)
            .ThenBy(row => row.Name));
    }

    /// <inheritdoc />
    public Task<SchemataSecurity?> CreateAsync(SchemataSecurity? security, CancellationToken ct = default) {
        if (security is null) {
            return Task.FromResult<SchemataSecurity?>(null);
        }

        _rows.Add(security);
        return Task.FromResult<SchemataSecurity?>(security);
    }

    /// <inheritdoc />
    public Task UpdateAsync(SchemataSecurity? security, CancellationToken ct = default) { return Task.CompletedTask; }

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
