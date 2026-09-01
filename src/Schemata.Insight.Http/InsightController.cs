using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Exceptions;
using Schemata.Insight.Foundation;
using Schemata.Insight.Skeleton;
using Schemata.Messaging.Skeleton;

namespace Schemata.Insight.Http;

/// <summary>
///     Exposes the federated read query endpoint per
///     <seealso href="https://google.aip.dev/136">AIP-136: Custom methods</seealso>, dispatching through
///     the registered Insight request handler and translating Insight rejections into AIP-193 errors.
/// </summary>
[ApiController]
public sealed class InsightController : ControllerBase
{
    private readonly JsonSerializerOptions _json;
    private readonly IServiceProvider      _services;

    /// <summary>Wires the controller with request-handler resolution and the host JSON options used for responses.</summary>
    /// <param name="services">The scoped provider resolving the query handler.</param>
    /// <param name="json">The host JSON serializer options.</param>
    public InsightController(IServiceProvider services, IOptions<JsonSerializerOptions> json) {
        _services = services;
        _json     = json.Value;
    }

    /// <summary>Plans and executes a federated read query.</summary>
    /// <param name="request">The query request.</param>
    [HttpPost("~/v1/insight:query")]
    public async Task<IActionResult> QueryAsync([FromBody] QueryInsightRequest request) {
        QueryInsightResponse response;
        try {
            request.Principal = HttpContext.User;
            var dispatcher = _services.GetRequiredService<IRequestDispatcher>();
            response = await dispatcher.SendAsync<QueryInsightRequest, QueryInsightResponse>(request, HttpContext.RequestAborted);
        } catch (InsightValidationException ex) {
            throw Translate(ex);
        }

        return new JsonResult(response, _json);
    }

    private static SchemataException Translate(InsightValidationException ex) {
        var code = ex.Reason switch {
            InsightReasons.UnknownSourceName => 404,
            InsightReasons.Unimplemented     => 501,
            var _                            => 400,
        };

        return new(code, ex.Reason, ex.Message);
    }
}
