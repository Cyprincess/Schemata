namespace Schemata.Abstractions.Errors;

/// <summary>
///     Describes a single precondition that was not met.
/// </summary>
public class PreconditionViolation
{
    /// <summary>
    ///     Gets or sets the type of precondition that was violated (e.g., "TENANT").
    /// </summary>
    public virtual string? Type { get; set; }

    /// <summary>
    ///     Gets or sets the subject of the precondition (e.g., "request").
    /// </summary>
    public virtual string? Subject { get; set; }

    /// <summary>
    ///     Gets or sets a human-readable description of the violation.
    /// </summary>
    public virtual string? Description { get; set; }
}
