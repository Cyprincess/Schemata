namespace Schemata.Abstractions.Errors;

/// <summary>
///     Describes a single quota limit that was exceeded.
/// </summary>
public class QuotaViolation
{
    /// <summary>
    ///     Gets or sets the subject that exceeded the quota (e.g., "client:192.168.1.1").
    /// </summary>
    public virtual string? Subject { get; set; }

    /// <summary>
    ///     Gets or sets a human-readable description of the quota violation.
    /// </summary>
    public virtual string? Description { get; set; }
}
