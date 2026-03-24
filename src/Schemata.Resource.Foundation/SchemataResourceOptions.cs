using System;
using System.Collections.Generic;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Foundation;

/// <summary>
/// Configuration options for the Schemata resource system.
/// </summary>
public sealed class SchemataResourceOptions
{
    /// <summary>
    /// Gets the registered resources keyed by their entity type handle.
    /// </summary>
    public Dictionary<RuntimeTypeHandle, ResourceAttribute> Resources { get; } = [];

    /// <summary>
    /// Gets or sets whether create-request validation is globally suppressed.
    /// </summary>
    public bool SuppressCreateValidation { get; set; }

    /// <summary>
    /// Gets or sets whether update-request validation is globally suppressed.
    /// </summary>
    public bool SuppressUpdateValidation { get; set; }

    /// <summary>
    /// Gets or sets whether freshness (ETag) checks and generation are globally suppressed.
    /// </summary>
    public bool SuppressFreshness { get; set; }
}
