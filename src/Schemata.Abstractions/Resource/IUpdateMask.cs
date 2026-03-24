namespace Schemata.Abstractions.Resource;

/// <summary>
///     Indicates that a request supports partial updates via a field mask.
/// </summary>
public interface IUpdateMask
{
    /// <summary>
    ///     Gets or sets the comma-separated list of field paths to update.
    /// </summary>
    string? UpdateMask { get; set; }
}
