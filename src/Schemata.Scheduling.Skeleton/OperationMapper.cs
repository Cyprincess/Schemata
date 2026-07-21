using Schemata.Abstractions.Resource;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Skeleton;

/// <summary>
///     Maps a <see cref="SchemataJobExecution" /> row to the AIP-151
///     <see cref="Operation" /> wire envelope.
/// </summary>
public static class OperationMapper
{
    // google.rpc.Code values (https://github.com/googleapis/googleapis/blob/master/google/rpc/code.proto).
    private const int CodeCancelled = 1;
    private const int CodeUnknown   = 2;

    /// <summary>Creates an AIP-151 operation envelope from a scheduler execution row.</summary>
    public static Operation FromExecution(SchemataJobExecution execution) {
        var operation = new Operation {
            Name          = execution.Name ?? execution.Uid.ToString("n"),
            CanonicalName = execution.CanonicalName ?? $"operations/{execution.Uid:n}",
            Done          = execution.State.IsTerminal(),
            Metadata = new() {
                Method    = execution.Method,
                Job       = execution.Job,
                StartTime = execution.StartTime,
                EndTime   = execution.EndTime,
            },
        };

        if (execution.State == ExecutionState.Succeeded) {
            operation.Response = new() { Output = execution.Output };
        } else if (execution.State == ExecutionState.Failed) {
            operation.Error = new() { Code = CodeUnknown, Message = execution.RecentError };
        } else if (execution.State == ExecutionState.Cancelled) {
            operation.Error = new() { Code = CodeCancelled, Message = execution.RecentError ?? "Operation was cancelled." };
        }

        return operation;
    }
}
