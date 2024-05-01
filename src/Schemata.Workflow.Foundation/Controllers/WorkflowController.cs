using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advices;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Options;
using Schemata.Mapping.Skeleton;
using Schemata.Workflow.Foundation.Advices;
using Schemata.Workflow.Skeleton.Entities;
using Schemata.Workflow.Skeleton.Managers;
using Schemata.Workflow.Skeleton.Models;

namespace Schemata.Workflow.Foundation.Controllers;

[Authorize]
[ApiController]
[Route("~/[controller]")]
public sealed class WorkflowController(
    ILogger<WorkflowController>              logger,
    ISimpleMapper                            mapper,
    IOptionsMonitor<SchemataWorkflowOptions> options,
    IOptions<SchemataResourceOptions>        resources,
    IServiceProvider                         services) : ControllerBase
{
    private static readonly ConcurrentDictionary<Type, Type> RequestToInstance = [];

    private readonly SchemataResourceOptions                  _resources = resources.Value;

    private EmptyResult EmptyResult { get; } = new();

    [HttpPost]
    public async Task<IActionResult> Submit(WorkflowRequest<IStateful> request) {
        var ctx = new AdviceContext();

        if (!await Advices<IWorkflowSubmitAdvice>.AdviseAsync(services, ctx, request, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        if (request.Instance is null) {
            return BadRequest();
        }

        var rt = request.Instance.GetType();
        if (!RequestToInstance.TryGetValue(rt, out var it)) {
            it = _resources.Resources.Where(r => r.Value.Request == rt).Select(r => r.Key).FirstOrDefault();
            if (it is null) {
                return BadRequest();
            }

            RequestToInstance[rt] = it;
        }

        var instance = mapper.Map<IStatefulEntity>(request.Instance, rt, it);
        if (instance is null) {
            return BadRequest();
        }

        var type = typeof(IWorkflowManager<,,>).MakeGenericType(options.CurrentValue.WorkflowType, options.CurrentValue.TransitionType, options.CurrentValue.WorkflowResponseType);

        var manager = (IWorkflowManager)services.GetRequiredService(type);

        var workflow = await manager.CreateAsync(instance);
        if (workflow is null) {
            return BadRequest();
        }

        var response = await manager.MapAsync(workflow, options.CurrentValue, User);
        if (response is null) {
            return NotFound();
        }

        return Ok(response);
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id) {
        var type = typeof(IWorkflowManager<,,>)
           .MakeGenericType(options.CurrentValue.WorkflowType, options.CurrentValue.TransitionType, options.CurrentValue.WorkflowResponseType);

        var manager = (IWorkflowManager)services.GetRequiredService(type);

        var workflow = await manager.FindAsync(id);
        if (workflow is null) {
            return NotFound();
        }

        var response = await manager.MapAsync(workflow, options.CurrentValue, User);
        if (response is null) {
            return NotFound();
        }

        return Ok(response);
    }

    [HttpPost("{id:long}")]
    public async Task<IActionResult> Raise(long id, IEvent request) {
        var type = typeof(IWorkflowManager<,,>)
           .MakeGenericType(options.CurrentValue.WorkflowType, options.CurrentValue.TransitionType, options.CurrentValue.WorkflowResponseType);

        var manager = (IWorkflowManager)services.GetRequiredService(type);

        var workflow = await manager.FindAsync(id);
        if (workflow is null) {
            return NotFound();
        }

        request.UpdatedById = User.GetUserId();
        request.UpdatedBy   = User.GetDisplayName();

        try {
            await manager.RaiseAsync(workflow, request);
        } catch (Exception e) {
            logger.LogError("Unable to update state: {Message}", e.Message);
            return BadRequest();
        }

        var response = await manager.MapAsync(workflow, options.CurrentValue, User);
        if (response is null) {
            return NotFound();
        }

        return Ok(response);
    }
}
