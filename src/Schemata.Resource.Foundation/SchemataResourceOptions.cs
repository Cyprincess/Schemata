using System;
using System.Collections.Generic;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Foundation;

/// <summary>
///     Configuration options for the resource system. Controls global validation suppression,
///     freshness suppression, authentication scheme, and stores registered resource descriptors.
/// </summary>
public sealed class SchemataResourceOptions
{
    /// <summary>
    ///     Gets the registered resources keyed by entity type handle.
    /// </summary>
    public Dictionary<RuntimeTypeHandle, ResourceAttribute> Resources { get; } = [];

    /// <summary>
    ///     Gets or sets whether create-request validation is globally suppressed.
    /// </summary>
    public bool SuppressCreateValidation { get; set; }

    /// <summary>
    ///     Gets or sets whether update-request validation is globally suppressed.
    /// </summary>
    public bool SuppressUpdateValidation { get; set; }

    /// <summary>
///     Gets or sets whether freshness (ETag) checks and generation are globally suppressed
///     per <seealso href="https://google.aip.dev/154">AIP-154: Resource freshness validation</seealso>.
    /// </summary>
    public bool SuppressFreshness { get; set; }

    /// <summary>
    ///     Gets or sets the authentication scheme for resource endpoints.
    ///     When <see langword="null" />, the application's default authentication scheme is used.
    /// </summary>
    public string? AuthenticationScheme { get; set; }
}
