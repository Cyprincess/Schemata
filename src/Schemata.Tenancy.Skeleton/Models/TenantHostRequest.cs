using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Tenancy.Skeleton.Models;

/// <summary>Create or update request body for <see cref="Entities.SchemataTenantHost" />.</summary>
public class TenantHostRequest : ICanonicalName, IFreshness, IChild
{
    /// <summary>HTTP Host header value that routes requests to the parent tenant.</summary>
    public string? Host { get; set; }

    #region IChild Members

    /// <inheritdoc />
    public string? Parent { get; set; }

    #endregion

    #region ICanonicalName Members

    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }

    #endregion

    #region IFreshness Members

    public string? EntityTag { get; set; }

    #endregion
}
