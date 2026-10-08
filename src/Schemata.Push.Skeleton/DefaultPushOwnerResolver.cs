using System;
using System.Linq;
using System.Security.Claims;

namespace Schemata.Push.Skeleton;

/// <summary>
///     Derives the owner canonical name from the first authenticated identity carrying a
///     <c>sub</c> or name-identifier claim. Applications with different identity shapes replace
/// </summary>
public sealed class DefaultPushOwnerResolver : IPushOwnerResolver
{
    public string? Resolve(ClaimsPrincipal? principal) {
        foreach (var identity in principal?.Identities ?? []) {
            if (!identity.IsAuthenticated) continue;
            var subject = identity.FindFirst("sub")?.Value ?? identity.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrWhiteSpace(subject)) return subject;
        }
        return null;
    }
}
