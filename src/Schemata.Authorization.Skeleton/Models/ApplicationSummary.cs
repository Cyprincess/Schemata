using System;
using Schemata.Abstractions.Entities;

namespace Schemata.Authorization.Skeleton.Models;

/// <summary>List item for <see cref="Entities.SchemataApplication" />.</summary>
public class ApplicationSummary : IIdentifier, ICanonicalName, ITimestamp
{
    /// <summary>OAuth 2.0 client identifier.</summary>
    public string? ClientId { get; set; }

    /// <summary>Application type, such as <c>"web"</c> or <c>"native"</c>.</summary>
    public string? ApplicationType { get; set; }

    /// <summary>Display name shown for the application.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Description shown for the application.</summary>
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
