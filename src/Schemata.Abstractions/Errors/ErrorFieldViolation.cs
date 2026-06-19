namespace Schemata.Abstractions.Errors;

/// <summary>
///     A single field-level validation failure describing the rejected field and reason, per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>.
/// </summary>
public class ErrorFieldViolation
{
    /// <summary>
    ///     Dot-separated path to the invalid field (e.g. <c>"user.address.zip_code"</c>).
    /// </summary>
    public virtual string? Field { get; set; }

    /// <summary>
    ///     Human-readable explanation of the validation failure.
    /// </summary>
    public virtual string? Description { get; set; }

    /// <summary>
    ///     Machine-readable reason code identifying the violated constraint
    ///     (e.g. <c>"REQUIRED"</c>, <c>"PATTERN"</c>).
    /// </summary>
    public virtual string? Reason { get; set; }
}
