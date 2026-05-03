namespace Schemata.Abstractions.Errors;

/// <summary>
///     A single precondition that was required for the operation but was not satisfied, per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>.
/// </summary>
public class PreconditionViolation
{
    /// <summary>
    ///     Category of precondition that failed (e.g. <c>"TENANT"</c>, <c>"ETAG"</c>).
    /// </summary>
    public virtual string? Type { get; set; }

    /// <summary>
    ///     Entity or resource whose precondition was violated
    ///     (e.g. <c>"request"</c>, <c>"project:123"</c>).
    /// </summary>
    public virtual string? Subject { get; set; }

    /// <summary>
    ///     Human-readable explanation of why the precondition was not met.
    /// </summary>
    public virtual string? Description { get; set; }
}
