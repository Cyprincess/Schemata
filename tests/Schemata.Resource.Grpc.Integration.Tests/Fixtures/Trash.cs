using System;
using System.ComponentModel.DataAnnotations;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Grpc.Integration.Tests.Fixtures;

[CanonicalName("trashes/{trash}")]
[Microsoft.EntityFrameworkCore.PrimaryKey(nameof(Uid))]
public sealed class Trash : IIdentifier, ICanonicalName, IConcurrency, IFreshness, IValidation, IUpdateMask, ISoftDelete
{
    public string? FullName { get; set; }
    public string? Name { get; set; }
    public string? CanonicalName { get; set; }
    [ConcurrencyCheck]
    public Guid Timestamp { get; set; }
    public string? EntityTag { get; set; }
    public Guid Uid { get; set; }
    public DateTime? DeleteTime { get; set; }
    public DateTime? PurgeTime { get; set; }
    public string? UpdateMask { get; set; }
    public bool ValidateOnly { get; set; }
}
