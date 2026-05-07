using System.Collections.Generic;

namespace Schemata.Identity.Skeleton.Claims;

/// <summary>
///     A dictionary that maps claim types to their associated <see cref="ClaimStore" /> values.
/// </summary>
public sealed class ClaimsStore : Dictionary<string, ClaimStore>
{
    /// <summary>
    ///     Adds a claim value under the specified type, creating the store entry if necessary.
    /// </summary>
    /// <param name="type">The claim type. Ignored when <see langword="null" /> or whitespace.</param>
    /// <param name="value">The claim value. Ignored when <see langword="null" /> or whitespace.</param>
    public void AddClaim(string? type, string? value) {
        if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(value)) {
            return;
        }

        if (!TryGetValue(type!, out var store)) {
            store       = [];
            this[type!] = store;
        }

        store.Add(value);
    }
}
