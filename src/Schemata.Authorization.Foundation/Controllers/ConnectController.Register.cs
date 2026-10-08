using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Queries;
using Schemata.Authorization.Skeleton.Models;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Controllers;

public partial class ConnectController
{
    [HttpPost("Register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest? request, CancellationToken ct) {
        var result = await dispatcher.SendAsync<RegisterEndpointQuery, RegistrationResponse>(
            new(RequireWellFormedMetadata(request), BearerToken()), ct);
        return new JsonResult(result) {
            StatusCode = 201,
        };
    }

    /// <summary>
    ///     Maps registration model-binding failures to stable OAuth errors: a malformed
    ///     <c>software_statement</c> field is <c>invalid_software_statement</c>; any other failure,
    ///     including a malformed body, is <c>invalid_client_metadata</c>.
    /// </summary>
    private RegisterRequest RequireWellFormedMetadata(RegisterRequest? request) {
        if (ModelState.IsValid && request is not null) {
            return request;
        }

        var statement = ModelState.Keys.Any(key => key.Contains("software_statement", StringComparison.OrdinalIgnoreCase));
        throw new OAuthException(
            statement ? OAuthErrors.InvalidSoftwareStatement : OAuthErrors.InvalidClientMetadata,
            statement ? SchemataResources.SOFTWARE_STATEMENT_NOT_WELL_FORMED : SchemataResources.INVALID_CLIENT_METADATA);
    }

    private string? BearerToken()
        => Request.Headers.Authorization.FirstOrDefault()?.StartsWith("Bearer ") == true
            ? Request.Headers.Authorization.FirstOrDefault()!["Bearer ".Length..].Trim()
            : null;

    [HttpGet("Register/{clientId}")]
    public async Task<IActionResult> RegisterRead(string clientId, CancellationToken ct) {
        var result = await dispatcher.SendAsync<RegisterReadQuery, RegistrationResponse?>(
            new(clientId, BearerToken()), ct);

        if (result is null) {
            Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\"";
            return Unauthorized();
        }

        return new JsonResult(result);
    }

    [HttpPut("Register/{clientId}")]
    public async Task<IActionResult> RegisterReplace(string clientId, [FromBody] RegisterRequest? request, CancellationToken ct) {
        var result = await dispatcher.SendAsync<RegisterReplaceQuery, RegistrationResponse?>(
            new(clientId, RequireWellFormedMetadata(request), BearerToken()), ct);

        if (result is null) {
            Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\"";
            return Unauthorized();
        }

        return new JsonResult(result);
    }

    [HttpDelete("Register/{clientId}")]
    public async Task<IActionResult> RegisterDelete(string clientId, CancellationToken ct) {
        var deleted = await dispatcher.SendAsync<RegisterDeleteQuery, bool>(
            new(clientId, BearerToken()), ct);

        if (!deleted) {
            Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\"";
            return Unauthorized();
        }

        // RFC 7592 §2.3: a successful delete returns 204 No Content.
        return NoContent();
    }

}
