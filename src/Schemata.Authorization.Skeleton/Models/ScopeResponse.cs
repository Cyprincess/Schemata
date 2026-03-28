using System.Collections.Generic;
using Schemata.Abstractions.Entities;

namespace Schemata.Authorization.Skeleton.Models;

public class ScopeResponse : IDescriptive
{
    /// <summary>Machine-readable scope identifier, e.g. "openid" or "profile".</summary>
    public string? Name { get; set; }

    #region IDescriptive Members

    /// <inheritdoc />
    public string? DisplayName { get; set; }

    /// <inheritdoc />
    public Dictionary<string, string>? DisplayNames { get; set; }

    /// <inheritdoc />
    public string? Description { get; set; }

    /// <inheritdoc />
    public Dictionary<string, string>? Descriptions { get; set; }

    #endregion
}
