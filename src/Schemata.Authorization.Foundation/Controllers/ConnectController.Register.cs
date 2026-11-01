using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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

        var statement = ModelState
            .Where(entry => entry.Key.Contains("software_statement", StringComparison.OrdinalIgnoreCase))
            .Any(entry => entry.Value is { Errors.Count: > 0 });
        throw new OAuthException(
            statement ? OAuthErrors.InvalidSoftwareStatement : OAuthErrors.InvalidClientMetadata,
            statement ? SchemataResources.SOFTWARE_STATEMENT_NOT_WELL_FORMED : SchemataResources.INVALID_CLIENT_METADATA);
    }

    /// <summary>
    ///     Extracts the bearer credential from the Authorization header, per RFC 6750 §2.1
    ///     over the general framework of RFC 9110 §11: the auth-scheme token matches
    ///     case-insensitively, the token value stays case-sensitive, and an absent header is
    ///     the only anonymous outcome. Multiple Authorization field lines (RFC 9110 §5.2:
    ///     <c>Authorization = credentials</c> is not a list) or a malformed credential
    ///     (RFC 9110 §11.4) are rejected with a 401 <c>invalid_token</c> challenge rather
    ///     than downgraded to anonymous. A single well-formed field whose credential only
    ///     violates the token68 charset (internal whitespace, RFC 9110 §11.2) reaches the
    ///     lookup and rejects there with the same 401 <c>invalid_token</c> shape.
    /// </summary>
    private string? BearerToken() {
        var values = Request.Headers.Authorization;
        switch (values.Count) {
            case 0:
                return null;
            case > 1:
                throw RejectMalformed();
        }

        var header = values.ToString();
        var space  = header.IndexOf(' ');
        if (space <= 0 || !header[..space].Equals(Schemes.Bearer, StringComparison.OrdinalIgnoreCase)) {
            throw RejectMalformed();
        }

        var token = header[(space + 1)..].Trim();
        return string.IsNullOrWhiteSpace(token) ? throw RejectMalformed() : token;

        static OAuthException RejectMalformed() {
            return new(
                OAuthErrors.InvalidToken,
                SchemataResources.INVALID_TOKEN,
                code: (int)HttpStatusCode.Unauthorized
            ) { Headers = new Dictionary<string, string> {
                    ["WWW-Authenticate"] = "Bearer",
                },
            };
        }
    }

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
