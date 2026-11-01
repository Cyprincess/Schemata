using System;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Http.Integration.Tests.Fixtures;

[CanonicalName("idempotentOrders/{idempotent_order}")]
[Microsoft.EntityFrameworkCore.PrimaryKey(nameof(Uid))]
public sealed class IdempotentOrder : IIdentifier, ICanonicalName
{
    public string? Note { get; set; }

    #region ICanonicalName Members

    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }

    #endregion

    #region IIdentifier Members

    public Guid Uid { get; set; }

    #endregion
}

public sealed class IdempotentOrderRequest : ICanonicalName, IRequestIdentification
{
    public string? Note { get; set; }

    #region ICanonicalName Members

    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }

    #endregion

    #region IRequestIdentification Members

    public string? RequestId { get; set; }

    #endregion
}
