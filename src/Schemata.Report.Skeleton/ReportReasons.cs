namespace Schemata.Report.Skeleton;

/// <summary>The well-known reason codes emitted by report contracts.</summary>
public static class ReportReasons
{
    /// <summary>The supplied operation has not reached a terminal state.</summary>
    public const string OperationNotComplete = "OPERATION_NOT_COMPLETE";

    /// <summary>The supplied operation completed with an error status.</summary>
    public const string OperationFailed = "OPERATION_FAILED";

    /// <summary>The completed operation omitted or contained an invalid report output payload.</summary>
    public const string InvalidOperationOutput = "INVALID_OPERATION_OUTPUT";
}