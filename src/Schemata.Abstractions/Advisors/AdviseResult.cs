namespace Schemata.Abstractions.Advisors;

/// <summary>
///     Controls advisor pipeline flow. Returned by
///     <see cref="IAdvisor{T1}.AdviseAsync" /> and its overloads.
/// </summary>
public enum AdviseResult
{
    /// <summary>
    ///     Proceed to the next advisor in the pipeline.
    /// </summary>
    Continue,

    /// <summary>
    ///     Abort the operation. No further advisors execute.
    /// </summary>
    Block,

    /// <summary>
    ///     The operation was handled by this advisor. No further advisors execute.
    /// </summary>
    Handle,
}
