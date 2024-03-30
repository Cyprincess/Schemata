using System;
using System.Collections.Generic;
using Schemata.Abstractions.Entities;

namespace Schemata.Authorization.Foundation.Entities;

public class SchemataAuthorization : IIdentifier, IConcurrency, ITimestamp
{
    public virtual long? ApplicationId { get; set; }

    public virtual string? Subject { get; set; }

    public virtual string? ClientId { get; set; }

    public virtual string? Type { get; set; }

    public virtual string? Status { get; set; }

    public virtual string? Properties { get; set; }

    public virtual List<string>? Scopes { get; set; }

    #region IConcurrency Members

    public Guid? Timestamp { get; set; }

    #endregion

    #region IIdentifier Members

    public long Id { get; set; }

    #endregion

    #region ITimestamp Members

    public DateTime? CreationDate     { get; set; }
    public DateTime? ModificationDate { get; set; }

    #endregion
}
