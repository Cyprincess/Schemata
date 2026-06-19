namespace Schemata.Abstractions.Resource;

/// <summary>
///     Marks a request as tolerant of missing resources when the corresponding AIP
///     defines create-on-update or delete-missing semantics.
/// </summary>
public interface IAllowMissing
{
    /// <summary>
    ///     When <see langword="true" />, missing-resource handling follows the request's AIP semantics.
    /// </summary>
    bool AllowMissing { get; set; }
}
