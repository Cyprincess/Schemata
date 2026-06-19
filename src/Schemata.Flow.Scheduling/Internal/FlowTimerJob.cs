using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Exceptions;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Scheduling.Skeleton;

namespace Schemata.Flow.Scheduling.Internal;

/// <summary>Scheduled job that fires a BPMN timer event into the <see cref="IProcessRuntime"/>.</summary>
public sealed class FlowTimerJob : IScheduledJob
{
    private readonly IProcessRuntime _runtime;

    /// <summary>Creates a scheduled job that triggers timers through the process runtime.</summary>
    public FlowTimerJob(IProcessRuntime runtime) {
        _runtime = runtime;
    }

    #region IScheduledJob Members

    public async Task ExecuteAsync(JobContext context, CancellationToken ct) {
        var processName = ExtractProcessName(context)
                       ?? throw new FailedPreconditionException(message: "Flow timer job is missing the 'processName' variable.");

        var timerDef = ExtractTimerDefinition(context)
                    ?? throw new FailedPreconditionException(message: "Flow timer job is missing the 'timerDef' variable.");

        await _runtime.TriggerEventAsync(processName, timerDef, ct: ct);
    }

    #endregion

    private static string? ExtractProcessName(JobContext context) {
        if (!context.Variables.TryGetValue("processName", out var value)) {
            return null;
        }

        return value switch {
            string s       => s,
            JsonElement je => je.GetString(),
            var _          => null,
        };
    }

    private static TimerDefinition? ExtractTimerDefinition(JobContext context) {
        if (!context.Variables.TryGetValue("timerDef", out var value)) {
            return null;
        }

        return value switch {
            TimerDefinition t => t,
            JsonElement je    => je.Deserialize<TimerDefinition>(),
            var _             => null,
        };
    }
}
