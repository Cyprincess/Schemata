using System;
using Schemata.Abstractions.Entities;

namespace Schemata.Authorization.Skeleton.Models;

/// <summary>List item for <see cref="Entities.SchemataApplication" />.</summary>
public class ApplicationSummary : IIdentifier, ICanonicalName, ITimestamp
{
    public string? ClientId { get; set; }

    public string? ApplicationType { get; set; }

    public string? DisplayName { get; set; }

    public string? Description { get; set; }

    #region ICanonicalName Members

    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }

    #endregion

    #region IIdentifier Members

    public Guid Uid { get; set; }

    #endregion

    #region ITimestamp Members

    public DateTime? CreateTime { get; set; }
    public DateTime? UpdateTime { get; set; }

    #endregion
}
