using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Entities;
using Schemata.Workflow.Skeleton;
using Schemata.Workflow.Skeleton.Managers;

namespace Schemata.Workflow.Foundation.Controllers;

[Authorize]
[ApiController]
[Route("~/[controller]")]
public class WorkflowController : ControllerBase
{
    private readonly ILogger<WorkflowController>              _logger;
    private readonly IOptionsMonitor<SchemataWorkflowOptions> _options;
    private readonly IServiceProvider                         _services;

    public WorkflowController(
        ILogger<WorkflowController>              logger,
        IOptionsMonitor<SchemataWorkflowOptions> options,
        IServiceProvider                         services) {
        _logger   = logger;
        _options  = options;
        _services = services;
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Read(long id) {
        var type = typeof(IWorkflowManager<,,>)
           .MakeGenericType(_options.CurrentValue.WorkflowType, _options.CurrentValue.TransitionType, _options.CurrentValue.WorkflowResponseType);

        var manager = (IWorkflowManager)_services.GetRequiredService(type);

        var workflow = await manager.FindAsync(id);
        if (workflow == null) {
            return NotFound();
        }

        var response = await manager.MapAsync(workflow, _options.CurrentValue, User);
        if (response == null) {
            return NotFound();
        }

        return Ok(response);
    }

    [HttpPost("{id:long}")]
    public async Task<IActionResult> Raise(long id, IEvent request) {
        var type = typeof(IWorkflowManager<,,>)
           .MakeGenericType(_options.CurrentValue.WorkflowType, _options.CurrentValue.TransitionType, _options.CurrentValue.WorkflowResponseType);

        var manager = (IWorkflowManager)_services.GetRequiredService(type);

        var workflow = await manager.FindAsync(id);
        if (workflow == null) {
            return NotFound();
        }

        request.UpdatedById = User.GetUserId();
        request.UpdatedBy   = User.GetDisplayName();

        try {
            await manager.RaiseAsync(workflow, request);
        } catch (Exception e) {
            _logger.LogError("Unable to update state: {Message}", e.Message);
            return BadRequest();
        }

        var response = await manager.MapAsync(workflow, _options.CurrentValue, User);
        if (response == null) {
            return NotFound();
        }

        return Ok(response);
    }
}
