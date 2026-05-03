using System.Collections.Generic;
using Schemata.Abstractions.Entities;

namespace Schemata.Authorization.Skeleton.Models;

/// <summary>Application summary returned in interaction UIs and consent screens.</summary>
public class ApplicationResponse : IDescriptive
{
    /// <summary>
    ///     OAuth 2.0 client identifier.
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-2.2">
    ///         RFC 6749: The OAuth 2.0 Authorization
    ///         Framework §2.2: Client Identifier
    ///     </seealso>
    /// </summary>
    public string? ClientId { get; set; }

    #region IDescriptive Members

    public string? DisplayName { get; set; }

    public Dictionary<string, string>? DisplayNames { get; set; }

    public string? Description { get; set; }

    public Dictionary<string, string>? Descriptions { get; set; }

    #endregion
}
