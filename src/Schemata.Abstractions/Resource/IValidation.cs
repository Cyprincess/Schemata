namespace Schemata.Abstractions.Resource;

/// <summary>
///     Marks a request as capable of dry-run validation: the server validates
///     the input and skips side effects.
/// </summary>
public interface IValidation
{
    /// <summary>
    ///     When <see langword="true" />, the request is a dry run.
    /// </summary>
    public bool ValidateOnly { get; set; }
}
