using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Common;
using Schemata.Flow.Foundation;
using Schemata.Flow.Skeleton.Models;
using Schemata.Scheduling.Skeleton;

namespace Schemata.Flow.Scheduling.Internal;

/// <summary>
///     Scheduled job that fires a BPMN timer catch by delegating to
///     <see cref="FlowRunner.RunEventAsync" />, which advances the addressed token through the full
///     transition unit of work: advisor chain, source projection, and follow-up event subscriptions.
/// </summary>
public sealed class FlowTimerJob : IScheduledJob
{
    private readonly IServiceProvider _services;

    public FlowTimerJob(IServiceProvider services) {
        _services = services;
    }

    #region IScheduledJob Members

    public async Task ExecuteAsync(JobContext context, CancellationToken ct) {
        var processName = RequireVariable(context, "processName");
        var tokenName   = RequireVariable(context, "tokenName");
        var timerDef    = RequireTimerDefinition(context);

        using var scope  = _services.CreateScope();
        var       runner = scope.ServiceProvider.GetRequiredService<FlowRunner>();
        await runner.RunEventAsync(processName, tokenName, timerDef, null, ct);
    }

    #endregion

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
