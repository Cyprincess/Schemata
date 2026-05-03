using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;

namespace Schemata.Authorization.Skeleton.Extensions;

/// <summary>
///     Extension methods for <see cref="IScopeManager{TScope}" />.
/// </summary>
public static class ScopeManagerExtensions
{
    /// <summary>Resolves a collection of scope names into their full <typeparamref name="TScope" /> entities.</summary>
    public static async IAsyncEnumerable<TScope> ResolveScopesAsync<TScope>(
        this IScopeManager<TScope>                 manager,
        ICollection<string>                        scopes,
        [EnumeratorCancellation] CancellationToken ct = default
    )
        where TScope : SchemataScope {
        ct.ThrowIfCancellationRequested();

        if (scopes.Count == 0) {
            yield break;
        }

        await foreach (var s in manager.ListAsync(scopes, ct)) {
            yield return s;
        }
    }
}
