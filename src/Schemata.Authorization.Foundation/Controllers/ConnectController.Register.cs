using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Schemata.Authorization.Foundation.Queries;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Foundation.Controllers;

public partial class ConnectController
{
    [HttpPost("Register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct) {
        var bearer = Request.Headers.Authorization.FirstOrDefault()?.StartsWith("Bearer ") == true
            ? Request.Headers.Authorization.FirstOrDefault()!["Bearer ".Length..].Trim()
            : null;

        var result = await dispatcher.SendAsync<RegisterEndpointQuery, RegistrationResponse>(
            new(request, bearer), ct);
        return new ObjectResult(result) {
            StatusCode = 201,
        };
    }

    [HttpGet("Register/{clientId}")]
    public async Task<IActionResult> RegisterRead(string clientId, CancellationToken ct) {
        var bearer = Request.Headers.Authorization.FirstOrDefault()?.StartsWith("Bearer ") == true
            ? Request.Headers.Authorization.FirstOrDefault()!["Bearer ".Length..].Trim()
            : null;

        var result = await dispatcher.SendAsync<RegisterReadQuery, RegistrationResponse?>(
            new(clientId, bearer), ct);

        if (result is null) {
            Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\"";
            return Unauthorized();
        }

        return Ok(result);
    }

}
