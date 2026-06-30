using System.Security.Claims;
using Schemata.Abstractions;
using Schemata.Common;

namespace Schemata.Flow.Foundation;

/// <summary>
///     Helpers shared by the Flow resource-method handlers: signed-in subject resolution for
///     audit columns and leaf-id generation for new rows.
/// </summary>
public static class FlowHandlerSupport
{
    /// <summary>Resolves the canonical signed-in subject of <paramref name="principal" /> for audit columns.</summary>
    public static string? ResolveUpdatedBy(ClaimsPrincipal? principal) {
        if (principal is null) {
            return null;
        }

        var sub = principal.FindFirst(SchemataConstants.Claims.Subject)?.Value;
        if (!string.IsNullOrWhiteSpace(sub)) {
            return $"users/{sub}";
        }

        return principal.Identity?.Name;
    }

    /// <summary>Generates a transient bare leaf id for new flow rows.</summary>
    public static string NewLeafId() {
        return Identifiers.NewUid().ToString("n");
    }
}
