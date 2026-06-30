using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Common;
using Schemata.Flow.Foundation;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Scheduling.Skeleton;

namespace Schemata.Flow.Scheduling.Internal;

/// <summary>
///     Scheduled job that fires a BPMN timer catch by invoking the engine-neutral
///     <see cref="CorrelateMessageHandler" />-style path: load process + tokens, resolve the keyed
///     <see cref="IFlowRuntime" />, advance via <see cref="IFlowRuntime.TriggerAsync" />, persist.
/// </summary>
public sealed class FlowTimerJob : IScheduledJob
{
    private readonly IServiceProvider _services;

    public FlowTimerJob(IServiceProvider services) {
        _services = services;
    }

    #region IScheduledJob Members

    public async Task ExecuteAsync(JobContext context, CancellationToken ct) {
        var processName = ExtractProcessName(context);
        if (processName is null) {
            throw new FailedPreconditionException(
                SchemataResources.FLOW_TIMER_MISSING_VARIABLE,
                new Dictionary<string, string?> { ["variable"] = "processName" }
            );
        }

        var timerDef = ExtractTimerDefinition(context);
        if (timerDef is null) {
            throw new FailedPreconditionException(
                SchemataResources.FLOW_TIMER_MISSING_VARIABLE,
                new Dictionary<string, string?> { ["variable"] = "timerDef" }
            );
        }

        using var scope = _services.CreateScope();
        var       sp    = scope.ServiceProvider;

        var persistence = sp.GetRequiredService<ProcessPersistence>();
        var registry    = sp.GetRequiredService<IProcessRegistry>();
        var notifier    = sp.GetRequiredService<ProcessLifecycleNotifier>();

        var process = await persistence.FindAsync(sp, processName, ct);
        if (process is null) {
            throw new NotFoundException(
                SchemataResources.PROCESS_NOT_REGISTERED,
                new Dictionary<string, string?> { ["name"] = processName }
            );
        }

        var reg = registry.GetRegistration(process.DefinitionName);
        if (reg is null) {
            throw new NotFoundException(
                SchemataResources.PROCESS_NOT_REGISTERED,
                new Dictionary<string, string?> { ["name"] = process.DefinitionName }
            );
        }

        var engine = sp.GetKeyedService<IFlowRuntime>(reg.Engine);
        if (engine is null) {
            throw new FailedPreconditionException(
                SchemataResources.FLOW_RUNTIME_NOT_REGISTERED,
                new Dictionary<string, string?> { ["engine"] = reg.Engine }
            );
        }

        ProcessSnapshot? snapshot = null;
        try {
            await persistence.ExecuteAsync(sp, async (flow, c) => {
                var tokens = new List<SchemataProcessToken>();
                await foreach (var token in flow.Tokens.ListAsync<SchemataProcessToken>(q => q.Where(t => t.Process == process.Name), c)) {
                    tokens.Add(token);
                }

                var execution = new FlowExecutionContext(flow.UnitOfWork, sp);
                snapshot = await engine.TriggerAsync(reg.Definition, process, tokens, execution, timerDef, null, null, c);
                await persistence.PersistSnapshotAsync(flow, snapshot, c);
            }, ct);
        } catch (Exception ex) {
            await notifier.NotifyFailedAsync(process, ex, ct);
            throw;
        }

        await notifier.NotifyTransitionedAsync(snapshot!, ct);

        if (string.Equals(process.State, "Completed", StringComparison.OrdinalIgnoreCase)) {
            await notifier.NotifyTerminatedAsync(process, ct);
        }
    }

    #endregion

    private static string? ExtractProcessName(JobContext context) {
        return context.Variables.TryGetValue("processName", out var value) && !string.IsNullOrEmpty(value)
            ? value
            : null;
    }

    private static TimerDefinition? ExtractTimerDefinition(JobContext context) {
        if (!context.Variables.TryGetValue("timerDef", out var value) || string.IsNullOrEmpty(value)) {
            return null;
        }

        return JsonSerializer.Deserialize<TimerDefinition>(value, SchemataJson.Default);
    }
}
