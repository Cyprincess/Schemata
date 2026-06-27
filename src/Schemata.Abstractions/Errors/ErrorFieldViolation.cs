namespace Schemata.Abstractions.Errors;

/// <summary>
///     A single field-level validation failure describing the rejected field and reason, per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso> and the
///     <c>BadRequest.FieldViolation</c> message in
///     <see href="https://github.com/googleapis/googleapis/blob/master/google/rpc/error_details.proto">
///     google/rpc/error_details.proto</see>.
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
    ///     Machine-readable reason code identifying the violated constraint.
    /// </summary>
    /// <remarks>
    ///     Must match the AIP-193 regex <c>[A-Z][A-Z0-9_]+[A-Z0-9]</c> (UPPER_SNAKE_CASE,
    ///     ≤ 63 chars). Examples: <c>"REQUIRED"</c>, <c>"INVALID_FORMAT"</c>,
    ///     <c>"OUT_OF_RANGE"</c>. Identity error codes that arrive in PascalCase must be
    ///     normalized before assignment.
    /// </remarks>
    public virtual string? Reason { get; set; }

    /// <summary>
    ///     Localized error message safe to return to the end user, per
    ///     <c>BadRequest.FieldViolation.localized_message</c>. Populated when the framework
    ///     knows the caller locale and a translation is available.
    /// </summary>
    public virtual LocalizedMessageDetail? LocalizedMessage { get; set; }
}
