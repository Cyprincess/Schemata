namespace Schemata.Abstractions.Advisors;

/// <summary>
///     Controls the flow of the advisor pipeline after an advisor executes.
/// </summary>
public enum AdviseResult
{
    /// <summary>
    ///     The pipeline should continue to the next advisor.
    /// </summary>
    Continue,

    /// <summary>
    ///     The pipeline should stop; the operation is blocked.
    /// </summary>
    Block,

    /// <summary>
    ///     The pipeline should stop; the advisor has handled the operation.
    /// </summary>
    Handle,
}
