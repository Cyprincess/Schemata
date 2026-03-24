namespace Schemata.Abstractions.Errors;

/// <summary>
///     Describes a single field-level validation violation.
/// </summary>
public class ErrorFieldViolation
{
    /// <summary>
    ///     Gets or sets the field path that caused the violation.
    /// </summary>
    public virtual string? Field { get; set; }

    /// <summary>
    ///     Gets or sets a human-readable description of the violation.
    /// </summary>
    public virtual string? Description { get; set; }

    /// <summary>
    ///     Gets or sets the machine-readable reason code for the violation.
    /// </summary>
    public virtual string? Reason { get; set; }
}
