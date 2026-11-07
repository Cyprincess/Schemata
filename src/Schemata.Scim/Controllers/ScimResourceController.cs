using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Scim.Models;
using Schemata.Scim.Services;

namespace Schemata.Scim.Controllers;

/// <summary>Serves explicitly registered resource handlers through the shared SCIM HTTP grammar.</summary>
[ApiController]
[Route("~" + ScimConstants.BasePath + "/{resourceEndpoint:scimResource}")]
public sealed class ScimResourceController(
    IEnumerable<IScimResourceHandler> handlers,
    IOptions<ScimServiceProviderOptions> options,
    IScimProvisioningAuthorizer? authorizer = null
) : ScimControllerBase
{
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string resourceEndpoint, string id) {
        if (Find(resourceEndpoint) is not { } handler) {
            return NotFoundResource(resourceEndpoint);
        }

        if (!Authenticated()) {
            return UnauthorizedResult();
        }

        var resource = await handler.GetAsync(id, User, HttpContext.RequestAborted);
        return resource is null ? NotFoundResource(id) : ResourceResult(handler, resource, id);
    }

    [HttpGet]
    public async Task<IActionResult> List(string resourceEndpoint) {
        if (Find(resourceEndpoint) is not { } handler) {
            return NotFoundResource(resourceEndpoint);
        }

        if (!Authenticated()) {
            return UnauthorizedResult();
        }

        var query = Request.Query;
        var request = new ScimSearchRequest {
            Attributes = QueryAttributes("attributes"),
            ExcludedAttributes = QueryAttributes("excludedAttributes"),
            Filter = query["filter"].FirstOrDefault(),
            SortBy = query["sortBy"].FirstOrDefault(),
            SortOrder = query["sortOrder"].FirstOrDefault(),
            StartIndex = QueryInteger("startIndex", 1),
            Count = QueryInteger("count", options.Value.DefaultPageSize),
        };
        return await Search(handler, request);
    }

    [HttpPost(".search")]
    public async Task<IActionResult> Search(string resourceEndpoint, JsonObject body) {
        if (Find(resourceEndpoint) is not { } handler) {
            return NotFoundResource(resourceEndpoint);
        }

        if (!Authenticated()) {
            return UnauthorizedResult();
        }

        RequireSchema(body, ScimConstants.SearchRequestSchema);
        var request = JsonSerializer.Deserialize<ScimSearchRequest>(body, ScimJson.Options)
            ?? throw new ScimProtocolException(400, ScimConstants.InvalidSyntax, "The SearchRequest body is required.");
        if (body["count"] is null) {
            request.Count = options.Value.DefaultPageSize;
        }
        return await Search(handler, request);
    }

    [HttpPost]
    public async Task<IActionResult> Create(string resourceEndpoint, JsonObject body) {
        if (Find(resourceEndpoint) is not { } handler) {
            return NotFoundResource(resourceEndpoint);
        }

        if (!Authenticated()) {
            return UnauthorizedResult();
        }

        if (!await CanWrite(handler, "create")) {
            return Error(StatusCodes.Status403Forbidden, detail: "The provisioning write is not permitted.");
        }

        var input = ScimProjection.SanitizeInput(body, handler.Schemas);
        var resource = await handler.CreateAsync(input, User, HttpContext.RequestAborted);
        return ResourceResult(handler, resource, resource.Id, StatusCodes.Status201Created);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Replace(string resourceEndpoint, string id, JsonObject body) {
        if (Find(resourceEndpoint) is not { } handler) {
            return NotFoundResource(resourceEndpoint);
        }

        if (!Authenticated()) {
            return UnauthorizedResult();
        }

        if (!await CanWrite(handler, "replace")) {
            return Error(StatusCodes.Status403Forbidden, detail: "The provisioning write is not permitted.");
        }

        var input = ScimProjection.SanitizeInput(body, handler.Schemas);
        var resource = await handler.ReplaceAsync(id, input, User, HttpContext.RequestAborted);
        return resource is null ? NotFoundResource(id) : ResourceResult(handler, resource, id);
    }

    [HttpPatch("{id}")]
    public async Task<IActionResult> Patch(string resourceEndpoint, string id, JsonObject body) {
        if (Find(resourceEndpoint) is not { } handler) {
            return NotFoundResource(resourceEndpoint);
        }

        if (!Authenticated()) {
            return UnauthorizedResult();
        }

        if (!await CanWrite(handler, "patch")) {
            return Error(StatusCodes.Status403Forbidden, detail: "The provisioning write is not permitted.");
        }

        RequireSchema(body, ScimConstants.PatchOpSchema);
        var patch = JsonSerializer.Deserialize<ScimPatchDocument>(body, ScimJson.Options)
            ?? throw new ScimProtocolException(400, ScimConstants.InvalidSyntax, "The PatchOp body is required.");
        if (patch.Operations.Count > options.Value.MaxPatchOperations) {
            throw new ScimProtocolException(400, ScimConstants.InvalidValue, "The PATCH request exceeds the operation limit.");
        }

        foreach (var operation in patch.Operations) {
            ScimJson.RejectPassword(operation.Value);
            if (string.Equals(operation.Path, "password", StringComparison.OrdinalIgnoreCase)) {
                throw new ScimProtocolException(400, ScimConstants.InvalidValue, "The password attribute is not supported.");
            }
        }

        var resource = await handler.PatchAsync(id, patch, User, HttpContext.RequestAborted);
        return resource is null ? NotFoundResource(id) : ResourceResult(handler, resource, id);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string resourceEndpoint, string id) {
        if (Find(resourceEndpoint) is not { } handler) {
            return NotFoundResource(resourceEndpoint);
        }

        if (!Authenticated()) {
            return UnauthorizedResult();
        }

        if (!await CanWrite(handler, "delete")) {
            return Error(StatusCodes.Status403Forbidden, detail: "The provisioning write is not permitted.");
        }

        return await handler.DeleteAsync(id, User, HttpContext.RequestAborted) ? NoContent() : NotFoundResource(id);
    }

    [HttpPost("~" + ScimConstants.BasePath + "/.search")]
    public IActionResult RootSearch() {
        return Error(StatusCodes.Status501NotImplemented, detail: "Cross-resource search is not supported.");
    }

    [HttpPost("~" + ScimConstants.BasePath + "/Bulk")]
    public IActionResult Bulk() {
        return Error(StatusCodes.Status501NotImplemented, detail: "Bulk operations are not supported.");
    }

    [AcceptVerbs("GET", "POST", "PUT", "PATCH", "DELETE", Route = "~" + ScimConstants.BasePath + "/Me")]
    public IActionResult Me() {
        return Error(StatusCodes.Status501NotImplemented, detail: "The /Me alias is not supported.");
    }

    private async Task<IActionResult> Search(IScimResourceHandler handler, ScimSearchRequest request) {
        if (!string.IsNullOrEmpty(request.SortBy)) {
            return Error(StatusCodes.Status501NotImplemented, detail: "Sorting is not supported.");
        }

        if (request.Attributes is { Count: > 0 } && request.ExcludedAttributes is { Count: > 0 }) {
            throw new ScimProtocolException(400, ScimConstants.InvalidValue, "attributes and excludedAttributes are mutually exclusive.");
        }

        if (request.Filter is { } filter) {
            if (Encoding.UTF8.GetByteCount(filter) > options.Value.MaxFilterLength) {
                throw new ScimProtocolException(400, ScimConstants.InvalidFilter, "The filter exceeds the configured length limit.");
            }

            request.FilterExpression = ScimFilterParser.Parse(filter, handler.Schemas, options.Value.MaxFilterDepth);
        }

        request.StartIndex = Math.Max(1, request.StartIndex);
        request.Count = Math.Min(options.Value.MaxPageSize, Math.Max(0, request.Count));
        var result = await handler.SearchAsync(request, User, HttpContext.RequestAborted);
        var resources = result.Resources.Select(resource => Project(handler, resource, request.Attributes, request.ExcludedAttributes, resource.Id)).ToList();
        var response = new ListResponse<JsonObject> {
            TotalResults = result.TotalResults,
            StartIndex = request.StartIndex,
            ItemsPerPage = resources.Count,
            Resources = resources,
        };
        return Scim(response);
    }

    private IActionResult ResourceResult(IScimResourceHandler handler, ScimResource resource, string? id, int status = StatusCodes.Status200OK) {
        var attributes = QueryAttributes("attributes");
        var excluded = QueryAttributes("excludedAttributes");
        var projected = Project(handler, resource, attributes, excluded, id);
        var location = ScimResults.Location(Request, handler.ResourceType.Endpoint.Trim('/'), id, options.Value.BasePath);
        Response.Headers["Content-Location"] = location;
        if (status == StatusCodes.Status201Created) {
            Response.Headers.Location = location;
        }

        return new JsonResult(projected, ScimJson.Options) { StatusCode = status, ContentType = ScimConstants.MediaType };
    }

    private JsonObject Project(
        IScimResourceHandler handler,
        ScimResource resource,
        IEnumerable<string>? attributes,
        IEnumerable<string>? excludedAttributes,
        string? id
    ) {
        var included = attributes?.ToArray() ?? [];
        var excluded = excludedAttributes?.ToArray() ?? [];
        var result = ScimJson.ToJsonObject(resource);
        id ??= result["id"]?.GetValue<string>();
        result = ScimProjection.Apply(result, handler.Schemas, included, excluded);

        if (included.Length == 0 && !excluded.Contains("meta", StringComparer.OrdinalIgnoreCase)
            || included.Any(value => string.Equals(value, "meta", StringComparison.OrdinalIgnoreCase))) {
            var meta = result["meta"] as JsonObject ?? new JsonObject();
            result["meta"] = meta;
            meta["resourceType"] = handler.ResourceType.Name;
            meta.Remove("version");
            meta["location"] = ScimResults.Location(Request, handler.ResourceType.Endpoint.Trim('/'), id, options.Value.BasePath);
        }

        return result;
    }

    private IScimResourceHandler? Find(string endpoint) {
        return handlers.FirstOrDefault(handler => MatchesEndpoint(handler, endpoint));
    }

    internal static bool MatchesEndpoint(IScimResourceHandler handler, string? endpoint) {
        return string.Equals(handler.ResourceType.Endpoint.Trim('/'), endpoint, StringComparison.OrdinalIgnoreCase);
    }

    private bool Authenticated() => User.Identity?.IsAuthenticated == true;

    private async Task<bool> CanWrite(IScimResourceHandler handler, string operation) {
        return authorizer is null || await authorizer.CanWriteAsync(User, handler.ResourceType.Name, operation, HttpContext.RequestAborted);
    }

    private IActionResult UnauthorizedResult() {
        return Error(StatusCodes.Status401Unauthorized, detail: "Authentication is required for SCIM resources.");
    }

    private IActionResult NotFoundResource(string id) {
        return Error(StatusCodes.Status404NotFound, detail: $"The SCIM resource '{id}' was not found.");
    }

    private string[] QueryAttributes(string name) {
        return Request.Query[name].SelectMany(value => (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                       .Where(value => value.Length > 0)
                       .ToArray();
    }

    private int QueryInteger(string name, int fallback) {
        var value = Request.Query[name].FirstOrDefault();
        if (string.IsNullOrEmpty(value)) {
            return fallback;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)) {
            throw new ScimProtocolException(400, ScimConstants.InvalidValue, $"The {name} query parameter must be an integer.");
        }

        return result;
    }

    private static void RequireSchema(JsonObject body, string schema) {
        if (body["schemas"] is not JsonArray schemas
            || !schemas.Any(value => string.Equals(value?.GetValue<string>(), schema, StringComparison.Ordinal))) {
            throw new ScimProtocolException(400, ScimConstants.InvalidSyntax, $"The {schema} schema is required.");
        }
    }
}

internal sealed class ScimResourceRouteConstraint : IRouteConstraint
{
    public bool Match(
        HttpContext? httpContext,
        IRouter? route,
        string routeKey,
        RouteValueDictionary values,
        RouteDirection routeDirection
    ) {
        if (httpContext is null || !values.TryGetValue(routeKey, out var value)) {
            return false;
        }

        return httpContext.RequestServices.GetServices<IScimResourceHandler>()
            .Any(handler => ScimResourceController.MatchesEndpoint(handler, value as string ?? value?.ToString()));
    }
}
