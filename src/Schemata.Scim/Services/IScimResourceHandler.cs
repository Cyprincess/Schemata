using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Scim.Models;

namespace Schemata.Scim.Services;

/// <summary>Adapts one explicitly registered resource type to the shared SCIM HTTP grammar.</summary>
public interface IScimResourceHandler
{
    ScimResourceType ResourceType { get; }

    IReadOnlyList<ScimSchema> Schemas { get; }

    Task<ScimResource?> GetAsync(string id, ClaimsPrincipal principal, CancellationToken ct);

    Task<ScimSearchResult> SearchAsync(ScimSearchRequest request, ClaimsPrincipal principal, CancellationToken ct);

    Task<ScimResource> CreateAsync(JsonObject body, ClaimsPrincipal principal, CancellationToken ct);

    Task<ScimResource?> ReplaceAsync(string id, JsonObject body, ClaimsPrincipal principal, CancellationToken ct);

    Task<ScimResource?> PatchAsync(string id, ScimPatchDocument patch, ClaimsPrincipal principal, CancellationToken ct);

    Task<bool> DeleteAsync(string id, ClaimsPrincipal principal, CancellationToken ct);
}
