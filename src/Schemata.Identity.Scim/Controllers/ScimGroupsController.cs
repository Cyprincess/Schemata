using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Schemata.Identity.Scim.Features;
using Schemata.Identity.Scim.Services;
using Schemata.Scim;
using Schemata.Scim.Controllers;
using Schemata.Scim.Models;

namespace Schemata.Identity.Scim.Controllers;

/// <summary>
///     Serves the RFC 7644 Group resource endpoints under <see cref="ScimConstants.BasePath" />.
///     Requests use the host's authentication and the SCIM authorization policy.
/// </summary>
/// <param name="groups">The group adapter selected by the identity SCIM feature.</param>
/// <param name="options">The SCIM page-size and authorization settings.</param>
[ApiController]
[TypeFilter(typeof(ScimUsersAuthorizationFilter))]
[Route("~" + ScimConstants.BasePath + "/Groups")]
public sealed class ScimGroupsController(
    IScimGroupService                     groups,
    IOptions<SchemataIdentityScimOptions> options
) : ScimControllerBase
{
    /// <summary>Creates a group.</summary>
    /// <param name="body">The SCIM Group request body.</param>
    /// <param name="ct">A cancellation token.</param>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] JsonElement body, CancellationToken ct) {
        try {
            var group    = WithLocation(await groups.CreateAsync(ScimGroupRequest.Parse(body), ct));
            var location = Location("Groups", group.Id);
            var result   = ScimResults.Json(group);
            result.StatusCode                    = StatusCodes.Status201Created;
            Response.Headers.Location            = location;
            Response.Headers["Content-Location"] = location;

            return result;
        } catch (ScimGroupException error) {
            return Error(error.Status, error.ScimType, error.Message);
        }
    }

    /// <summary>Returns one group.</summary>
    /// <param name="id">The SCIM id.</param>
    /// <param name="ct">A cancellation token.</param>
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct) {
        var group = await groups.FindAsync(id, ct);

        return group is null ? NotFound(id) : Scim(WithLocation(group));
    }

    /// <summary>Lists groups.</summary>
    /// <param name="filter">A filter expression; the first-phase protocol rejects filtering.</param>
    /// <param name="startIndex">The 1-based index of the first returned group.</param>
    /// <param name="count">The maximum number of returned groups.</param>
    /// <param name="ct">A cancellation token.</param>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? filter,
        [FromQuery] int?    startIndex,
        [FromQuery] int?    count,
        CancellationToken   ct
    ) {
        if (filter is not null) {
            return Error(StatusCodes.Status400BadRequest, ScimConstants.InvalidFilter, "Filtering is not supported.");
        }

        var settings = options.Value;
        var limit    = count ?? settings.DefaultPageSize;
        if (limit > settings.MaxPageSize) {
            limit = settings.MaxPageSize;
        }

        var page      = await groups.ListAsync(startIndex ?? 1, limit, ct);
        var resources = page.Resources.Select(WithLocation).ToList();

        return Scim(new ListResponse<ScimGroupResource> {
            TotalResults = page.TotalResults,
            StartIndex   = page.StartIndex,
            ItemsPerPage = resources.Count,
            Resources    = resources,
        });
    }

    /// <summary>Replaces a group and its complete membership; a missing id is not created.</summary>
    /// <param name="id">The SCIM id.</param>
    /// <param name="body">The SCIM Group request body.</param>
    /// <param name="ct">A cancellation token.</param>
    [HttpPut("{id}")]
    public async Task<IActionResult> Replace(string id, [FromBody] JsonElement body, CancellationToken ct) {
        try {
            var group = await groups.ReplaceAsync(id, ScimGroupRequest.Parse(body), ct);

            return group is null ? NotFound(id) : Scim(WithLocation(group));
        } catch (ScimGroupException error) {
            return Error(error.Status, error.ScimType, error.Message);
        }
    }

    /// <summary>Reports that PATCH is not supported.</summary>
    [HttpPatch("{id}")]
    public IActionResult Patch() {
        return Error(StatusCodes.Status501NotImplemented, detail: "PATCH is not supported for this SCIM endpoint.");
    }

    /// <summary>Deletes a group.</summary>
    /// <param name="id">The SCIM id.</param>
    /// <param name="ct">A cancellation token.</param>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct) {
        try {
            return await groups.DeleteAsync(id, ct) ? NoContent() : NotFound(id);
        } catch (ScimGroupException error) {
            return Error(error.Status, error.ScimType, error.Message);
        }
    }

    private ScimGroupResource WithLocation(ScimGroupResource group) {
        if (group.Meta is not { } meta) {
            throw new InvalidOperationException("A SCIM group requires service provider metadata.");
        }

        var members = group.Members.Select(member => member with { Ref = Location("Users", member.Value) })
                          .ToList();

        return group with {
            Members = members,
            Meta    = meta with { Location = Location("Groups", group.Id) },
        };
    }

    private IActionResult NotFound(string id) {
        return Error(StatusCodes.Status404NotFound, detail: $"Group '{id}' was not found.");
    }
}
