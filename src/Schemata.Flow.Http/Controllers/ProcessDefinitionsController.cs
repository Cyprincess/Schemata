using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Resource;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Http.Controllers;

[ApiController]
[Route("~/v1/processes:definitions")]
public sealed class ProcessDefinitionsController(
    IProcessRegistry                registry,
    IOptions<JsonSerializerOptions> json
) : ControllerBase
{
    [HttpGet]
    public IActionResult ListProcessDefinitions() {
        var entities = registry.GetRegisteredProcesses()
                               .Select(n => new ProcessDefinitionInfo { CanonicalName = $"definitions/{n}" })
                               .ToList();
        return new JsonResult(new ListResultBase<ProcessDefinitionInfo> { Entities = entities }, json.Value);
    }
}
