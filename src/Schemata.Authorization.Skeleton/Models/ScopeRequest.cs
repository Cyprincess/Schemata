using System.Collections.Generic;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Authorization.Skeleton.Models;

/// <summary>Create or update request body for <see cref="Entities.SchemataScope" /> resources.</summary>
public class ScopeRequest : ICanonicalName, IDescriptive, IFreshness
{
    /// <summary>API resources that this scope grants access to.</summary>
    public ICollection<string>? Resources { get; set; }

    #region ICanonicalName Members

    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }

    #endregion

    #region IDescriptive Members

    public string?                      DisplayName  { get; set; }
    public Dictionary<string, string?>? DisplayNames { get; set; }
    public string?                      Description  { get; set; }
    public Dictionary<string, string?>? Descriptions { get; set; }

    #endregion

    #region IFreshness Members

    public string? EntityTag { get; set; }

    #endregion
}
