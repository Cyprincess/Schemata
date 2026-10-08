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
using Schemata.Entity.Repository;
using Schemata.Flow.Foundation;
using Schemata.Flow.Foundation.Commands;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Commands;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Attributes;
using Schemata.Scheduling.Skeleton.Entities;
namespace Schemata.Flow.Scheduling.Runtime;

/// <summary>
///     Scheduled job that fires a BPMN timer catch through the unkeyed Flow event request handler.
///     A firing that no longer matches its registration (cancelled, or replaced by a newer
///     generation) is rejected with <see cref="SchemataResources.FLOW_TIMER_STALE_FIRING" />
///     before the branch is completed.
/// </summary>
[ScheduledJob(JobKey)]
public sealed class FlowTimerJob : IScheduledJob
{
    /// <summary>Stable scheduler key persisted on BPMN timer-catch job and execution rows.</summary>
    public const string JobKey = "schemata.flow.timer";

    private readonly IServiceProvider _services;

    public FlowTimerJob(IServiceProvider services) {
        _services = services;
    }

    #region IScheduledJob Members

    public async Task ExecuteAsync(JobContext context, CancellationToken ct) {
        var processName = RequireVariable(context, "processName");
        var tokenName   = RequireVariable(context, "tokenName");
        var timerDef    = RequireTimerDefinition(context);
        var elementName = RequireVariable(context, "elementName");

        using var scope = _services.CreateScope();
        await EnsureCurrentRegistrationAsync(scope.ServiceProvider, context, processName, elementName, ct);

        var dispatcher = scope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
        await dispatcher.SendAsync<ResourceMethodRequest<SchemataProcess, RunEventRequest, ProcessSnapshot>, ProcessSnapshot>(
            new(FlowOperations.RunEvent, processName, new(processName, tokenName, timerDef, Payload: null) {
                Principal = FlowSystemPrincipal.Instance,
            }, FlowSystemPrincipal.Instance), ct);
    }

    #endregion

    // Cancellation pauses the registration and re-arming rotates its schedule version, so a
    // firing is current only while the row that produced it stays active under the same version.
    private static async Task EnsureCurrentRegistrationAsync(
        IServiceProvider  services,
        JobContext        context,
        string            processName,
        string            elementName,
        CancellationToken ct
    ) {
        var stale = new FailedPreconditionException(
            SchemataResources.FLOW_TIMER_STALE_FIRING,
            new Dictionary<string, string?> { ["name"] = processName, ["element"] = elementName });

        if (string.IsNullOrEmpty(context.Job)) {
            throw stale;
        }

        var jobs         = services.GetRequiredService<IRepository<SchemataJob>>();
        var registration = await jobs.FirstOrDefaultAsync(query => query.Where(row => row.CanonicalName == context.Job), ct);
        if (registration is null || registration.State is not JobState.Active) {
            throw stale;
        }

        var fired = context.Execution?.ScheduleVersion ?? Guid.Empty;
        if (fired == Guid.Empty || fired != registration.ScheduleVersion) {
            throw stale;
        }
    }

    private static string RequireVariable(JobContext context, string name) {
        if (context.Variables.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value)) {
            return value;
        }

        throw new FailedPreconditionException(
            SchemataResources.FLOW_TIMER_MISSING_VARIABLE,
            new Dictionary<string, string?> { ["variable"] = name }
        );
    }

    private static TimerDefinition RequireTimerDefinition(JobContext context) {
        var value = RequireVariable(context, "timerDef");
        return JsonSerializer.Deserialize<TimerDefinition>(value, SchemataJson.Default)
            ?? throw new FailedPreconditionException(
                SchemataResources.FLOW_TIMER_MISSING_VARIABLE,
                new Dictionary<string, string?> { ["variable"] = "timerDef" }
            );
    }
}
