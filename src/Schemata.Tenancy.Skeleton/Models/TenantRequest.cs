using System.Collections.Generic;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Tenancy.Skeleton.Models;

/// <summary>Create/update request body for <see cref="Entities.SchemataTenant" />.</summary>
public class TenantRequest : ICanonicalName, IDescriptive, IFreshness
{
    #region ICanonicalName Members

    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }

    #endregion

    #region IDescriptive Members

    public string?                     DisplayName  { get; set; }
    public Dictionary<string, string>? DisplayNames { get; set; }
    public string?                     Description  { get; set; }
    public Dictionary<string, string>? Descriptions { get; set; }

    #endregion

    #region IFreshness Members

    public string? EntityTag { get; set; }

    #endregion
}
