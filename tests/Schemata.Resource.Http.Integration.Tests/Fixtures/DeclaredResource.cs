using System;
using System.ComponentModel.DataAnnotations;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Http.Integration.Tests.Fixtures;

[CanonicalName("httpNotes/{httpNote}")]
[Microsoft.EntityFrameworkCore.PrimaryKey(nameof(Uid))]
[Resource(typeof(HttpNote))]
[HttpResource]
public class HttpNote : IIdentifier, ICanonicalName, IConcurrency
{
    public Guid Uid { get; set; }
    public string? Name { get; set; }
    public string? CanonicalName { get; set; }
    public string? Label { get; set; }
    [ConcurrencyCheck]
    public Guid Timestamp { get; set; }
}

[CanonicalName("grpcNotes/{grpcNote}")]
[Microsoft.EntityFrameworkCore.PrimaryKey(nameof(Uid))]
[Resource(typeof(GrpcNote))]
[GrpcResource]
public class GrpcNote : IIdentifier, ICanonicalName, IConcurrency
{
    public Guid Uid { get; set; }
    public string? Name { get; set; }
    public string? CanonicalName { get; set; }
    public string? Label { get; set; }
    [ConcurrencyCheck]
    public Guid Timestamp { get; set; }
}
