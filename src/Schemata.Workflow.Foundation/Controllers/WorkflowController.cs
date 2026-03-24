using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Advice;
using Schemata.Common;
using Schemata.Mapping.Skeleton;
using Schemata.Workflow.Foundation.Advisors;
using Schemata.Workflow.Skeleton;
using Schemata.Workflow.Skeleton.Entities;
using Schemata.Workflow.Skeleton.Managers;
using Schemata.Workflow.Skeleton.Models;

namespace Schemata.Workflow.Foundation.Controllers;

/// <summary>
/// API controller that exposes workflow operations: Get, Submit, and Raise.
/// </summary>
/// <remarks>
/// Each action runs its corresponding advisor pipeline (<see cref="IWorkflowGetAdvisor"/>,
/// <see cref="IWorkflowSubmitAdvisor"/>, <see cref="IWorkflowRaiseAdvisor"/>) before
/// delegating to the <see cref="IWorkflowManager"/>.
/// </remarks>
[Authorize]
[ApiController]
[Route("~/[controller]")]
public sealed class WorkflowController : ControllerBase
{
    private readonly ILogger<WorkflowController>              _logger;
    private readonly ISimpleMapper                            _mapper;
    private readonly IOptionsMonitor<SchemataWorkflowOptions> _options;

    private readonly IServiceProvider _sp;

    /// <summary>
    /// Initializes a new instance of the workflow controller.
    /// </summary>
    /// <param name="sp">The service provider.</param>
    /// <param name="mapper">The object mapper.</param>
    /// <param name="options">The workflow options monitor.</param>
    /// <param name="logger">The logger.</param>
    public WorkflowController(
        IServiceProvider                         sp,
        ISimpleMapper                            mapper,
        IOptionsMonitor<SchemataWorkflowOptions> options,
        ILogger<WorkflowController>              logger
    ) {
        _sp      = sp;
        _mapper  = mapper;
        _options = options;
        _logger  = logger;
    }

    private EmptyResult EmptyResult { get; } = new();

    /// <summary>
    /// Retrieves a workflow by identifier, running the Get advisor pipeline.
    /// </summary>
    /// <param name="id">The workflow identifier.</param>
    /// <returns>The workflow response, or 404 if not found.</returns>
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

        var ctx = new AdviceContext(_sp);

        switch (await Advisor.For<IWorkflowGetAdvisor>()
                             .RunAsync(ctx, workflow, HttpContext, HttpContext.RequestAborted)) {
            case AdviseResult.Block:
            case AdviseResult.Handle:
                return EmptyResult;
            case AdviseResult.Continue:
                break;
        }

        var response = await manager.MapAsync(workflow, _options.CurrentValue, User);
        if (response is null) {
            return NotFound();
        }

        return Ok(response);
    }

    /// <summary>
    /// Submits a new workflow, running the Submit advisor pipeline.
    /// </summary>
    /// <param name="request">The workflow creation request containing the entity type and instance data.</param>
    /// <returns>The created workflow response, or 400 on validation failure.</returns>
    [HttpPost]
    public async Task<IActionResult> Submit(WorkflowRequest<IStateful> request) {
        var ctx = new AdviceContext(_sp);

        switch (await Advisor.For<IWorkflowSubmitAdvisor>()
                             .RunAsync(ctx, request, HttpContext, HttpContext.RequestAborted)) {
            case AdviseResult.Block:
            case AdviseResult.Handle:
                return EmptyResult;
            case AdviseResult.Continue:
                break;
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

    /// <summary>
    /// Raises an event on an existing workflow, running the Get and Raise advisor pipelines.
    /// </summary>
    /// <param name="id">The workflow identifier.</param>
    /// <param name="request">The event data to raise.</param>
    /// <returns>The updated workflow response, or 404/400 on failure.</returns>
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

        var ctx = new AdviceContext(_sp);

        switch (await Advisor.For<IWorkflowGetAdvisor>()
                             .RunAsync(ctx, workflow, HttpContext, HttpContext.RequestAborted)) {
            case AdviseResult.Block:
            case AdviseResult.Handle:
                return EmptyResult;
            case AdviseResult.Continue:
                break;
        }

        switch (await Advisor.For<IWorkflowRaiseAdvisor>()
                             .RunAsync(ctx, workflow, request, HttpContext, HttpContext.RequestAborted)) {
            case AdviseResult.Block:
            case AdviseResult.Handle:
                return EmptyResult;
            case AdviseResult.Continue:
                break;
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
