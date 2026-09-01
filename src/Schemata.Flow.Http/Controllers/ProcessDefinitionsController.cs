using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Resource;
using Schemata.Flow.Foundation.Commands;
using Schemata.Flow.Skeleton.Models;
using Schemata.Messaging.Skeleton;

namespace Schemata.Flow.Http.Controllers;

/// <summary>Lists Flow process definitions over HTTP.</summary>
[ApiController]
[Route("~/v1/processes:definitions")]
public sealed class ProcessDefinitionsController(
    IQueryDispatcher                dispatcher,
    IOptions<JsonSerializerOptions> json
) : ControllerBase
{
    /// <summary>Lists registered Flow process definitions.</summary>
    [HttpGet]
    public async Task<IActionResult> ListProcessDefinitions() {
        var results = await dispatcher.SendAsync<ListProcessDefinitionsQuery, IReadOnlyList<ProcessDefinitionInfo>>(
            new ListProcessDefinitionsQuery(), HttpContext.RequestAborted);
        return new JsonResult(new ListResultBase<ProcessDefinitionInfo> { Entities = results.ToList() }, json.Value);
    }
}
