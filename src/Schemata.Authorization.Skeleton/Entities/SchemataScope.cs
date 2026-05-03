using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;

namespace Schemata.Authorization.Skeleton.Entities;

/// <summary>
///     Represents an OAuth 2.0 scope registered with the authorization server.
///     Scopes define the permissions an application can request,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-3.3">
///         RFC 6749: The OAuth 2.0 Authorization
///         Framework §3.3: Access Token Scope
///     </seealso>
///     .
/// </summary>
[DisplayName("Scope")]
[Table("SchemataScopes")]
[CanonicalName("scopes/{scope}")]
public class SchemataScope : IIdentifier, ICanonicalName, IDescriptive, IConcurrency, ITimestamp
{
    /// <summary>API resources that this scope grants access to.</summary>
    public virtual ICollection<string>? Resources { get; set; }

    #region ICanonicalName Members

    public virtual string? Name { get; set; }

    public virtual string? CanonicalName { get; set; }

    #endregion

    #region IConcurrency Members

    public virtual Guid? Timestamp { get; set; }

    #endregion

    #region IDescriptive Members

    public virtual string? DisplayName { get; set; }

    public virtual Dictionary<string, string>? DisplayNames { get; set; }

    public virtual string? Description { get; set; }

    public virtual Dictionary<string, string>? Descriptions { get; set; }

    #endregion

    #region IIdentifier Members

    public virtual long Id { get; set; }

    #endregion

    #region ITimestamp Members

    public virtual DateTime? CreateTime { get; set; }

    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
