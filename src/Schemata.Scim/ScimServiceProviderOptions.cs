using System.Collections.Generic;
using Schemata.Scim.Models;

namespace Schemata.Scim;

/// <summary>
///     Host-supplied values for the service provider configuration resource.
/// </summary>
public sealed class ScimServiceProviderOptions
{
    public string BasePath { get; set; } = ScimConstants.BasePath;

    public int DefaultPageSize { get; set; } = 100;

    public int MaxPageSize { get; set; } = 200;

    public long MaxRequestBodySize { get; set; } = 1024 * 1024;

    public int MaxFilterLength { get; set; } = 8 * 1024;

    public int MaxFilterDepth { get; set; } = 32;

    public int MaxPatchOperations { get; set; } = 100;

    /// <summary>The provider's human-consumable help documentation URI.</summary>
    public string? DocumentationUri { get; set; }

    /// <summary>The authentication schemes the provider accepts.</summary>
    public IList<ScimAuthenticationScheme> AuthenticationSchemes { get; } = new List<ScimAuthenticationScheme>();
}
