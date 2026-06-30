using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Schemata.Abstractions.Entities;

namespace Schemata.Flow.Integration.Tests.Fixtures;

[Table("Orders")]
[CanonicalName("orders/{order}")]
[PrimaryKey(nameof(Uid))]
public sealed class Order : IIdentifier, ICanonicalName, IConcurrency, IStateful
{
    public string? TaskValue { get; set; }

    #region ICanonicalName Members

    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }

    #endregion

    #region IConcurrency Members

    [ConcurrencyCheck]
    public Guid Timestamp { get; set; }

    #endregion

    #region IIdentifier Members

    public Guid Uid { get; set; }

    #endregion

    #region IStateful Members

    public string? State { get; set; }

    #endregion
}
