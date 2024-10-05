using System.Collections.Generic;

namespace Schemata.Identity.Skeleton.Claims;

public sealed class ClaimsStore : Dictionary<string, ClaimStore>
{
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
