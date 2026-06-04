using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Tenancy.Skeleton.Models;

/// <summary>Create/update request body for <see cref="Entities.SchemataTenantHost" />.</summary>
public class TenantHostRequest : ICanonicalName, IFreshness
{
    /// <summary>Parent tenant's canonical name per AIP-122, e.g. <c>tenants/acme</c>.</summary>
    public string? Parent { get; set; }

    public string? Host { get; set; }

    #region ICanonicalName Members

    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }

    #endregion

    #region IFreshness Members

    public string? EntityTag { get; set; }

    #endregion
}
