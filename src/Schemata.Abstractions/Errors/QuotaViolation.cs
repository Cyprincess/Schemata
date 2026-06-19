namespace Schemata.Abstractions.Errors;

/// <summary>
///     A single quota limit exceeded by a particular subject, per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>.
/// </summary>
public class QuotaViolation
{
    /// <summary>
    ///     Identifier of the entity that exceeded the quota
    ///     (e.g. <c>"client:192.168.1.1"</c>, <c>"project:my-project"</c>).
    /// </summary>
    public virtual string? Subject { get; set; }

    /// <summary>
    ///     Human-readable description of the exceeded quota and amount.
    /// </summary>
    public virtual string? Description { get; set; }
}
