using System.ComponentModel;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Errors;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Skeleton;

[DisplayName("Operation")]
[CanonicalName("operations/{operation}")]
public sealed class SchemataOperation : ICanonicalName
{
    public bool Done { get; set; }

    public ErrorBody? Error { get; set; }

    public SchemataOperationResponse? Response { get; set; }

    public SchemataOperationMetadata? Metadata { get; set; }

    public static SchemataOperation FromExecution(SchemataJobExecution execution) {
        var operation = new SchemataOperation {
            Name          = execution.Name ?? execution.Uid.ToString("N"),
            CanonicalName = execution.CanonicalName ?? $"operations/{execution.Uid:N}",
            Done          = IsTerminal(execution.State),
            Metadata = new() {
                Job       = execution.Job,
                StartTime = execution.StartTime,
                EndTime   = execution.EndTime,
            },
        };

        if (execution.State == ExecutionState.Succeeded) {
            operation.Response = new() { Output = execution.Output };
        } else if (execution.State == ExecutionState.Failed) {
            operation.Error = new() { Code = "UNKNOWN", Message = execution.RecentError };
        } else if (execution.State == ExecutionState.Cancelled) {
            operation.Error = new() { Code = "CANCELLED", Message = execution.RecentError ?? "Operation was cancelled." };
        }

        return operation;
    }

    #region ICanonicalName Members

    public string? Name { get; set; }

    public string? CanonicalName { get; set; }

    #endregion

    private static bool IsTerminal(ExecutionState state) {
        return state is ExecutionState.Succeeded or ExecutionState.Failed or ExecutionState.Cancelled;
    }
}