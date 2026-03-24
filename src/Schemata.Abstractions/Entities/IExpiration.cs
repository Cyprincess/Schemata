using System;

namespace Schemata.Abstractions.Entities;

/// <summary>
///     Indicates that an entity can expire at a scheduled time.
/// </summary>
public interface IExpiration
{
    /// <summary>
    ///     Gets or sets the time at which the entity expires.
    /// </summary>
    DateTime? ExpireTime { get; set; }
}
