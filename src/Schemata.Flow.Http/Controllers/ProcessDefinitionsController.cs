using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Resource;
using Schemata.Flow.Foundation;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Http.Controllers;

/// <summary>Lists Flow process definitions over HTTP.</summary>
[ApiController]
[Route("~/v1/processes:definitions")]
public sealed class ProcessDefinitionsController(
    ProcessDefinitionQueryService   query,
    IOptions<JsonSerializerOptions> json
) : ControllerBase
{
    /// <summary>Lists registered Flow process definitions.</summary>
    [HttpGet]
    public IActionResult ListProcessDefinitions() {
        var entities = query.ListProcessDefinitions()
                            .Select(n => new ProcessDefinitionInfo { CanonicalName = n.CanonicalName })
                            .ToList();
        return new JsonResult(new ListResultBase<ProcessDefinitionInfo> { Entities = entities }, json.Value);
    }
}
