using System;

namespace Schemata.Abstractions.Entities;

/// <summary>
///     Provides a unique identifier suitable for use as a primary key.
/// </summary>
public interface IIdentifier
{
    /// <summary>
    ///     The unique identifier.
    /// </summary>
    Guid Uid { get; set; }
}
