namespace Schemata.Abstractions.Entities;

/// <summary>
///     Provides a unique numeric identifier suitable for use as a primary key.
/// </summary>
public interface IIdentifier
{
    /// <summary>
    ///     The unique numeric identifier.
    /// </summary>
    long Id { get; set; }
}
