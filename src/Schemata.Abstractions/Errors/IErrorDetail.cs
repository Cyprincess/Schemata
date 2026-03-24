namespace Schemata.Abstractions.Errors;

/// <summary>
///     Represents a typed error detail following the Google API error model.
/// </summary>
public interface IErrorDetail
{
    /// <summary>
    ///     Gets the fully-qualified type URL for this error detail (e.g., "type.googleapis.com/google.rpc.BadRequest").
    /// </summary>
    public string Type { get; }
}
