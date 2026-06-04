using System;
using System.Collections.Generic;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Identity.Skeleton.Models;

/// <summary>Get response body for <see cref="Entities.SchemataRole" />.</summary>
public class RoleDetail : IIdentifier, ICanonicalName, IDescriptive, ITimestamp, IFreshness
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

    #region IIdentifier Members

    public Guid Uid { get; set; }

    #endregion

    #region ITimestamp Members

    public DateTime? CreateTime { get; set; }
    public DateTime? UpdateTime { get; set; }

    #endregion
}
