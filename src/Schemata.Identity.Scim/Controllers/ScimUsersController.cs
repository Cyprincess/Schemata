using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using Schemata.Identity.Scim.Features;
using Schemata.Identity.Scim.Services;
using Schemata.Scim;
using Schemata.Scim.Controllers;
using Schemata.Scim.Models;

namespace Schemata.Identity.Scim.Controllers;

/// <summary>
///     Serves the RFC 7644 User resource endpoints under <see cref="ScimConstants.BasePath" />.
///     Requests use the host's authentication and the SCIM authorization policy.
/// </summary>
/// <param name="users">The user adapter selected by the identity SCIM feature.</param>
/// <param name="options">The SCIM page-size and authorization settings.</param>
[ApiController]
[TypeFilter(typeof(ScimUsersAuthorizationFilter))]
[Route("~" + ScimConstants.BasePath + "/Users")]
public sealed class ScimUsersController(
    IScimUserService                      users,
    IOptions<SchemataIdentityScimOptions> options
) : ScimControllerBase
{
    /// <summary>Creates a user.</summary>
    /// <param name="body">The SCIM User request body.</param>
    /// <param name="ct">A cancellation token.</param>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] JsonElement body, CancellationToken ct) {
        try {
            var user     = WithLocation(await users.CreateAsync(ScimUserRequest.Parse(body), ct));
            var location = user.Meta!.Location!;
            var result   = ScimResults.Json(user);
            result.StatusCode                    = StatusCodes.Status201Created;
            Response.Headers.Location            = location;
            Response.Headers["Content-Location"] = location;

            return result;
        } catch (ScimUserException error) {
            return Error(error.Status, error.ScimType, error.Message);
        }
    }

    /// <summary>Returns one user.</summary>
    /// <param name="id">The SCIM id.</param>
    /// <param name="ct">A cancellation token.</param>
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct) {
        var user = await users.FindAsync(id, ct);

        return user is null ? NotFound(id) : Scim(WithLocation(user));
    }

    /// <summary>Lists users.</summary>
    /// <param name="filter">A filter expression; the first-phase protocol rejects filtering.</param>
    /// <param name="startIndex">The 1-based index of the first returned user.</param>
    /// <param name="count">The maximum number of returned users.</param>
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

        var page      = await users.ListAsync(startIndex ?? 1, limit, ct);
        var resources = page.Resources.Select(WithLocation).ToList();

        return Scim(new ListResponse<ScimUserResource> {
            TotalResults = page.TotalResults,
            StartIndex   = page.StartIndex,
            ItemsPerPage = resources.Count,
            Resources    = resources,
        });
    }

    /// <summary>Replaces a user; a missing id is not created.</summary>
    /// <param name="id">The SCIM id.</param>
    /// <param name="body">The SCIM User request body.</param>
    /// <param name="ct">A cancellation token.</param>
    [HttpPut("{id}")]
    public async Task<IActionResult> Replace(string id, [FromBody] JsonElement body, CancellationToken ct) {
        try {
            var user = await users.ReplaceAsync(id, ScimUserRequest.Parse(body), ct);

            return user is null ? NotFound(id) : Scim(WithLocation(user));
        } catch (ScimUserException error) {
            return Error(error.Status, error.ScimType, error.Message);
        }
    }

    /// <summary>Reports that PATCH is not supported.</summary>
    [HttpPatch("{id}")]
    public IActionResult Patch() {
        return Error(StatusCodes.Status501NotImplemented, detail: "PATCH is not supported for this SCIM endpoint.");
    }

    /// <summary>Deletes a user.</summary>
    /// <param name="id">The SCIM id.</param>
    /// <param name="ct">A cancellation token.</param>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct) {
        try {
            return await users.DeleteAsync(id, ct) ? NoContent() : NotFound(id);
        } catch (ScimUserException error) {
            return Error(error.Status, error.ScimType, error.Message);
        }
    }

    private ScimUserResource WithLocation(ScimUserResource user) {
        var groups = user.Groups.Select(group => group with { Ref = Location("Groups", group.Value) })
                         .ToList();

        return user with {
            Groups = groups,
            Meta   = user.Meta! with { Location = Location("Users", user.Id) },
        };
    }

    private IActionResult NotFound(string id) {
        return Error(StatusCodes.Status404NotFound, detail: $"User '{id}' was not found.");
    }
}

/// <summary>
///     Enforces the configured SCIM authorization policy before a SCIM resource action runs and
///     renders failures as SCIM error bodies. Without a configured policy the host's default policy
///     applies.
/// </summary>
public sealed class ScimUsersAuthorizationFilter(
    IAuthorizationService                  authorization,
    IAuthorizationPolicyProvider          policies,
    IOptions<SchemataIdentityScimOptions> options
) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context) {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true) {
            context.Result = ScimResults.Error(401, detail: "Authentication is required.");

            return;
        }

        var name   = options.Value.AuthorizationPolicy;
        var policy = string.IsNullOrWhiteSpace(name)
                         ? await policies.GetDefaultPolicyAsync()
                         : await policies.GetPolicyAsync(name);
        if (policy is null) {
            context.Result = ScimResults.Error(403, detail: $"The SCIM authorization policy '{name}' is not configured.");

            return;
        }

        var result = await authorization.AuthorizeAsync(user, policy);
        if (!result.Succeeded) {
            context.Result = ScimResults.Error(403, detail: "The authenticated principal is not permitted to provision SCIM resources.");
        }
    }
}
