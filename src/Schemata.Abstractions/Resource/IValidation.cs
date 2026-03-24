namespace Schemata.Abstractions.Resource;

/// <summary>
///     Indicates that a request supports validation-only mode (dry run).
/// </summary>
public interface IValidation
{
    /// <summary>
    ///     Gets or sets whether the request should only be validated without executing.
    /// </summary>
    public bool ValidateOnly { get; set; }
}
