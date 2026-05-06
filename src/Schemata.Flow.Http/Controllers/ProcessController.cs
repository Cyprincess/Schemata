using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Schemata.Entity.Repository;
using Schemata.Flow.Foundation;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.Skeleton.Utilities;

namespace Schemata.Flow.Http.Controllers;

/// <summary>
///     A custom controller for process flow operations exposing BPMN 2.0
///     standard operations: start, complete, correlate message, throw signal,
///     and terminate process instances.
/// </summary>
[ApiController]
[Route("~/processes")]
public class ProcessController : ControllerBase
{
    private readonly IRepository<SchemataProcess>           _processes;
    private readonly IProcessRegistry                       _registry;
    private readonly ProcessRuntime                         _runtime;
    private readonly IRepository<SchemataProcessTransition> _transitions;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ProcessController" /> class.
    /// </summary>
    public ProcessController(
        ProcessRuntime                         runtime,
        IRepository<SchemataProcess>           processes,
        IRepository<SchemataProcessTransition> transitions,
        IProcessRegistry                       registry,
        JsonSerializerOptions                  json
    ) {
        _runtime     = runtime;
        _processes   = processes;
        _transitions = transitions;
        _registry    = registry;
        JsonOptions  = json;
    }

    protected JsonSerializerOptions JsonOptions { get; }

    #region BPMN 2.0 Operations

    /// <summary>
    ///     Starts a new process instance from the specified definition.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> StartProcessInstanceAsync([FromBody] StartProcessInstanceRequest request) {
        var variables = request.Variables is not null
            ? VariableSerializer.Deserialize(request.Variables)
            : null;

        var process = await _runtime.StartProcessInstanceAsync(
            request.DefinitionName,
            request.DisplayName,
            request.Description,
            variables,
            HttpContext.User,
            HttpContext.RequestAborted
        );

        return new JsonResult(process, JsonOptions) { StatusCode = StatusCodes.Status201Created };
    }

    /// <summary>
    ///     Completes the current activity and auto-advances the process instance.
    /// </summary>
    [HttpPost("{name}:complete")]
    public async Task<IActionResult> CompleteActivityAsync(string name, [FromBody] CompleteActivityRequest? request) {
        var variables = request?.Variables is not null
            ? VariableSerializer.Deserialize(request.Variables)
            : null;

        var instance = await _runtime.CompleteActivityAsync(
            $"processes/{name}",
            variables,
            HttpContext.User,
            HttpContext.RequestAborted
        );

        return new JsonResult(instance);
    }

    /// <summary>
    ///     Correlates a named message to a specific process instance.
    /// </summary>
    [HttpPost("{name}:correlate")]
    public async Task<IActionResult> CorrelateMessageAsync(string name, [FromBody] CorrelateMessageRequest request) {
        var payload = request.Payload is not null
            ? VariableSerializer.Deserialize(request.Payload)
            : null;

        var instance = await _runtime.CorrelateMessageAsync(
            $"processes/{name}",
            request.MessageName,
            payload,
            HttpContext.User,
            HttpContext.RequestAborted
        );

        return new JsonResult(instance);
    }

    /// <summary>
    ///     Throws (broadcasts) a signal to all waiting process instances.
    /// </summary>
    [HttpPost(":throw")]
    public async Task<IActionResult> ThrowSignalAsync([FromBody] ThrowSignalRequest request) {
        var payload = request.Payload is not null
            ? VariableSerializer.Deserialize(request.Payload)
            : null;

        await _runtime.ThrowSignalAsync(request.SignalName, payload, HttpContext.User, HttpContext.RequestAborted);

        return NoContent();
    }

    /// <summary>
    ///     Terminates a process instance immediately.
    /// </summary>
    [HttpPost("{name}:terminate")]
    public async Task<IActionResult> TerminateProcessInstanceAsync(string name) {
        var instance = await _runtime.TerminateProcessInstanceAsync(
            $"processes/{name}",
            HttpContext.User,
            HttpContext.RequestAborted
        );

        return new JsonResult(instance);
    }

    #endregion

    #region Queries

    /// <summary>
    ///     Lists all process instances.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ListProcessInstancesAsync() {
        var items = new List<SchemataProcess>();
        await foreach (var item in _processes.ListAsync<SchemataProcess>(null, HttpContext.RequestAborted)) {
            items.Add(item);
        }

        return new JsonResult(items);
    }

    /// <summary>
    ///     Gets a single process instance by its canonical name.
    /// </summary>
    [HttpGet("{name}")]
    public async Task<IActionResult> GetProcessInstanceAsync(string name) {
        var fullName = $"processes/{name}";
        var process = await _processes.SingleOrDefaultAsync(
            q => q.Where(p => p.CanonicalName == fullName),
            HttpContext.RequestAborted
        );

        if (process is null) {
            return NotFound();
        }

        return new JsonResult(process);
    }

    /// <summary>
    ///     Lists all transition records for a process instance.
    /// </summary>
    [HttpGet("{name}/transitions")]
    public async Task<IActionResult> ListProcessInstanceTransitionsAsync(string name) {
        var processName = $"processes/{name}";
        var items       = new List<SchemataProcessTransition>();
        await foreach (var item in _transitions.ListAsync<SchemataProcessTransition>(
                           q => q.Where(t => t.ProcessName == processName),
                           HttpContext.RequestAborted
                       )) {
            items.Add(item);
        }

        return new JsonResult(items);
    }

    /// <summary>
    ///     Gets a single transition record by its canonical name.
    /// </summary>
    [HttpGet("{name}/transitions/{transition}")]
    public async Task<IActionResult> GetProcessInstanceTransitionAsync(string name, string transition) {
        var full = $"processes/{name}/transitions/{transition}";
        var t = await _transitions.SingleOrDefaultAsync(
            q => q.Where(t => t.CanonicalName == full),
            HttpContext.RequestAborted
        );

        if (t is null) {
            return NotFound();
        }

        return new JsonResult(t);
    }

    /// <summary>
    ///     Lists all registered process definitions.
    /// </summary>
    [HttpGet(":definitions")]
    public IActionResult ListProcessDefinitions() {
        var definitions = _registry.GetRegisteredProcesses();
        return new JsonResult(definitions);
    }

    #endregion
}
