using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Http.Integration.Tests.Fixtures;

[CanonicalName("trashes/{trash}")]
[PrimaryKey(nameof(Uid))]
public class Trash : IIdentifier, ICanonicalName, IConcurrency, IFreshness, IValidation, IUpdateMask, ISoftDelete
{
    public string? FullName { get; set; }
    public int     Age      { get; set; }
    public int     Grade    { get; set; }

    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }

    public Guid Uid { get; set; }

    [ConcurrencyCheck]
    public Guid Timestamp { get; set; }

    public string? EntityTag { get; set; }

    public bool ValidateOnly { get; set; }

    public string? UpdateMask { get; set; }

    public DateTime? DeleteTime { get; set; }

    public DateTime? PurgeTime { get; set; }
}
