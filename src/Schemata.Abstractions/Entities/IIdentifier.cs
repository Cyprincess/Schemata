using System;

namespace Schemata.Abstractions.Entities;

/// <summary>
///     Provides the strictly unique system-internal identity of an entity instance.
/// </summary>
public interface IIdentifier
{
    /// <summary>
    ///     Identifies this instance independently of its resource name and lifecycle.
    /// </summary>
    Guid Uid { get; set; }
}
