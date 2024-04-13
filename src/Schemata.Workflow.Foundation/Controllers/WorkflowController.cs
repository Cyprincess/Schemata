using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
public sealed class WorkflowController : ControllerBase
{
    private static readonly ConcurrentDictionary<Type, Type> Types = [];

    private readonly ILogger<WorkflowController>              _logger;
    private readonly ISimpleMapper                            _mapper;
    private readonly IOptionsMonitor<SchemataWorkflowOptions> _options;
    private readonly SchemataResourceOptions                  _resources;
    private readonly IServiceProvider                         _services;

    public WorkflowController(
        ILogger<WorkflowController>              logger,
        ISimpleMapper                            mapper,
        IOptionsMonitor<SchemataWorkflowOptions> options,
        IOptions<SchemataResourceOptions>        resources,
        IServiceProvider                         services) {
        _logger    = logger;
        _mapper    = mapper;
        _options   = options;
        _resources = resources.Value;
        _services  = services;
    }

    private EmptyResult EmptyResult { get; } = new();

    [HttpPost]
    public async Task<IActionResult> Submit(WorkflowRequest<IStateful> request) {
        if (!await Advices<IWorkflowSubmitAdvice>.AdviseAsync(_services, request, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        if (request.Instance is null) {
            return BadRequest();
        }

        var rt = request.Instance.GetType();
        if (!Types.TryGetValue(rt, out var it)) {
            it = _resources.Resources.Where(r => r.Value.Request == rt).Select(r => r.Key).FirstOrDefault();
            if (it is null) {
                return BadRequest();
            }

            Types[rt] = it;
        }

        var instance = _mapper.Map<IStatefulEntity>(request.Instance, rt, it);
        if (instance is null) {
            return BadRequest();
        }

        var type = typeof(IWorkflowManager<,,>).MakeGenericType(_options.CurrentValue.WorkflowType, _options.CurrentValue.TransitionType, _options.CurrentValue.WorkflowResponseType);

        var manager = (IWorkflowManager)_services.GetRequiredService(type);

        var workflow = await manager.CreateAsync(instance);
        if (workflow == null) {
            return BadRequest();
        }

        var response = await manager.MapAsync(workflow, _options.CurrentValue, User);
        if (response == null) {
            return NotFound();
        }

        return Ok(response);
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
