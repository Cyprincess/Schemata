using System.Collections.Generic;
using Schemata.Abstractions.Entities;

namespace Schemata.Authorization.Skeleton.Models;

/// <summary>Scope summary displayed on the consent screen.</summary>
public class ScopeResponse : IDescriptive
{
    /// <summary>Machine-readable scope identifier, e.g. <c>"openid"</c> or <c>"profile"</c>.</summary>
    public string? Name { get; set; }

    #region IDescriptive Members

    public string? DisplayName { get; set; }

    public Dictionary<string, string>? DisplayNames { get; set; }

    public string? Description { get; set; }

    public Dictionary<string, string>? Descriptions { get; set; }

    #endregion
}
