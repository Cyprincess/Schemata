using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Exceptions;
using Schemata.Insight.Foundation;
using Schemata.Insight.Skeleton;

namespace Schemata.Insight.Http;

/// <summary>
///     Exposes the federated read query endpoint per
///     <seealso href="https://google.aip.dev/136">AIP-136: Custom methods</seealso>, delegating to
///     <see cref="IInsightService" /> and translating Insight rejections into AIP-193 errors.
/// </summary>
[ApiController]
public sealed class InsightController : ControllerBase
{
    private readonly JsonSerializerOptions _json;
    private readonly IInsightService       _service;

    /// <summary>Wires the controller with the federated query service and the host JSON options used for streaming responses.</summary>
    /// <param name="service">The query service.</param>
    /// <param name="json">The host JSON serializer options.</param>
    public InsightController(IInsightService service, IOptions<JsonSerializerOptions> json) {
        _service = service;
        _json    = json.Value;
    }

    /// <summary>Plans and executes a federated read query.</summary>
    /// <param name="request">The query request.</param>
    [HttpPost("~/v1/insight:query")]
    public async Task<IActionResult> QueryAsync([FromBody] QueryInsightRequest request) {
        QueryInsightResponse response;
        try {
            response = await _service.QueryAsync(request, HttpContext.User, HttpContext.RequestAborted);
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

        return new SchemataException(code, ex.Reason, ex.Message);
    }
}
