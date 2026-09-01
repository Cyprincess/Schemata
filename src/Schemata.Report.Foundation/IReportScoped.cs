namespace Schemata.Report.Foundation;

/// <summary>
///     Marks a request that generates one named report. The key serializes all writers of the same
///     report; a null key marks an inline request with no report identity, which the Report.Actor
///     bridge leaves unwrapped.
/// </summary>
public interface IReportScoped
{
    /// <summary>The report name serializing all generations of the same report, or null for an inline request.</summary>
    string? ReportKey { get; }
}