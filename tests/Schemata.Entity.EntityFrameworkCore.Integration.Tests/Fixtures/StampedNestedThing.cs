using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;

namespace Schemata.Entity.EntityFrameworkCore.Integration.Tests.Fixtures;

/// <summary>
///     <see cref="NestedThing" /> with an <see cref="IConcurrency" /> stamp: pins that the
///     nested-value comparer never stages a phantom update that would rotate the stamp, and
///     that stale-token rejection still fires for an entity carrying a nested JSON column.
/// </summary>
[Table("StampedNestedThings")]
[PrimaryKey(nameof(Uid))]
public class StampedNestedThing : IIdentifier, ICanonicalName, IConcurrency
{
    [Column(TypeName = "TEXT")]
    public Dictionary<string, List<string>>? Map { get; set; }

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
}
