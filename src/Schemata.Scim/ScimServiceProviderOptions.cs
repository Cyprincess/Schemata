using System.Collections.Generic;
using Schemata.Scim.Models;

namespace Schemata.Scim;

/// <summary>
///     Host-supplied values for the service provider configuration resource.
/// </summary>
public sealed class ScimServiceProviderOptions
{
    /// <summary>The provider's human-consumable help documentation URI.</summary>
    public string? DocumentationUri { get; set; }

    /// <summary>The authentication schemes the provider accepts.</summary>
    public IList<ScimAuthenticationScheme> AuthenticationSchemes { get; } = new List<ScimAuthenticationScheme>();
}
