using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions;
using Schemata.Abstractions.Entities;

namespace Schemata.Entity.EntityFrameworkCore.Integration.Tests.Fixtures;

/// <summary>
///     Exercises the declared-column conversion contract on the EF Core bridge:
///     <see cref='Map' /> is a nested dictionary outside the automatic JSON eligibility
///     set but carries an explicit <c>[Column]</c> declaration.
/// </summary>
[Table("NestedThings")]
[PrimaryKey(nameof(Uid))]
public class NestedThing : IIdentifier, ICanonicalName
{
    [Column(TypeName = "TEXT")]
    public Dictionary<string, List<string>>? Map { get; set; }

    public Dictionary<string, string>? SimpleMap { get; set; }

    public List<string>? Tags { get; set; }

    #region ICanonicalName Members

    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }

    #endregion

    #region IIdentifier Members

    public Guid Uid { get; set; }

    #endregion
}
