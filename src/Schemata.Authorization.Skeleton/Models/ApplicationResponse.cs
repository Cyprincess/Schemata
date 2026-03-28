using System.Collections.Generic;
using Schemata.Abstractions.Entities;

namespace Schemata.Authorization.Skeleton.Models;

public class ApplicationResponse : IDescriptive
{
    /// <summary>OAuth 2.0 client identifier per RFC 6749 section 2.2.</summary>
    public string? ClientId { get; set; }

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
