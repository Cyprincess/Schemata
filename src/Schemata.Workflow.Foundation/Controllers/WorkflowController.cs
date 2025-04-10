using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
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
public sealed class WorkflowController : ControllerBase
{
    private readonly ILogger<WorkflowController>              _logger;
    private readonly ISimpleMapper                            _mapper;
    private readonly IOptionsMonitor<SchemataWorkflowOptions> _options;

    private readonly IServiceProvider _sp;

    public WorkflowController(
        IServiceProvider                         sp,
        ISimpleMapper                            mapper,
        IOptionsMonitor<SchemataWorkflowOptions> options,
        ILogger<WorkflowController>              logger) {
        _sp      = sp;
        _mapper  = mapper;
        _options = options;
        _logger  = logger;
    }

    private EmptyResult EmptyResult { get; } = new();

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id) {
        var type = typeof(IWorkflowManager<,,>).MakeGenericType(_options.CurrentValue.WorkflowType,
                                                                _options.CurrentValue.TransitionType,
                                                                _options.CurrentValue.WorkflowResponseType);

        var service = _sp.GetRequiredService(type);
        if (service is not IWorkflowManager manager) {
            throw new InvalidOperationException("Unable to resolve workflow manager.");
        }

        var workflow = await manager.FindAsync(id);
        if (workflow is null) {
            return NotFound();
        }

        var ctx = new AdviceContext();

        if (!await Advices<IWorkflowGetAdvice>.AdviseAsync(_sp, ctx, workflow, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        var response = await manager.MapAsync(workflow, _options.CurrentValue, User);
        if (response is null) {
            return NotFound();
        }

        return Ok(response);
    }

    [HttpPost]
    public async Task<IActionResult> Submit(WorkflowRequest<IStateful> request) {
        var ctx = new AdviceContext();

        if (!await Advices<IWorkflowSubmitAdvice>.AdviseAsync(_sp, ctx, request, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        if (request.Instance is null || string.IsNullOrWhiteSpace(request.Type)) {
            return BadRequest();
        }

        var rt = request.Instance.GetType();
        var it = AppDomainTypeCache.GetType(request.Type);
        if (it is null) {
            return BadRequest();
        }

        var instance = _mapper.Map<IStatefulEntity>(request.Instance, rt, it);
        if (instance is null) {
            return BadRequest();
        }

        var type = typeof(IWorkflowManager<,,>).MakeGenericType(_options.CurrentValue.WorkflowType,
                                                                _options.CurrentValue.TransitionType,
                                                                _options.CurrentValue.WorkflowResponseType);

        var service = _sp.GetRequiredService(type);
        if (service is not IWorkflowManager manager) {
            throw new InvalidOperationException("Unable to resolve workflow manager.");
        }

        var workflow = await manager.CreateAsync(instance, User);
        if (workflow is null) {
            return BadRequest();
        }

        var response = await manager.MapAsync(workflow, _options.CurrentValue, User);
        if (response is null) {
            return NotFound();
        }

        return Ok(response);
    }

    [HttpPost("{id:long}")]
    public async Task<IActionResult> Raise(long id, IEvent request) {
        var type = typeof(IWorkflowManager<,,>).MakeGenericType(_options.CurrentValue.WorkflowType,
                                                                _options.CurrentValue.TransitionType,
                                                                _options.CurrentValue.WorkflowResponseType);

        var service = _sp.GetRequiredService(type);
        if (service is not IWorkflowManager manager) {
            throw new InvalidOperationException("Unable to resolve workflow manager.");
        }

        var workflow = await manager.FindAsync(id);
        if (workflow is null) {
            return NotFound();
        }

        var ctx = new AdviceContext();

        if (!await Advices<IWorkflowGetAdvice>.AdviseAsync(_sp, ctx, workflow, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        if (!await Advices<IWorkflowRaiseAdvice>.AdviseAsync(_sp, ctx, workflow, request, HttpContext, HttpContext.RequestAborted)) {
            return EmptyResult;
        }

        try {
            await manager.RaiseAsync(workflow, request, User);
        } catch (Exception e) {
            _logger.LogError("Unable to update state: {Message}", e.Message);
            return BadRequest();
        }

        var response = await manager.MapAsync(workflow, _options.CurrentValue, User);
        if (response is null) {
            return NotFound();
        }

        return Ok(response);
    }
}
