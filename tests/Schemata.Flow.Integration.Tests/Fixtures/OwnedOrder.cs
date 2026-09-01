using System;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;

namespace Schemata.Flow.Integration.Tests.Fixtures;

[Table("OwnedOrders")]
[CanonicalName("ownedOrders/{ownedOrder}")]
[Microsoft.EntityFrameworkCore.PrimaryKey(nameof(Uid))]
public sealed class OwnedOrder : IIdentifier, ICanonicalName, IStateful, IOwnable, ISoftDelete
{
    public string? TaskValue { get; set; }

    #region ICanonicalName Members

    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }

    #endregion

    #region IIdentifier Members

    public Guid Uid { get; set; }

    #endregion

    #region IOwnable Members

    public string? Owner { get; set; }

    #endregion

    #region ISoftDelete Members

    public DateTime? DeleteTime { get; set; }
    public DateTime? PurgeTime  { get; set; }

    #endregion

    #region IStateful Members

    public string? State { get; set; }

    #endregion
}