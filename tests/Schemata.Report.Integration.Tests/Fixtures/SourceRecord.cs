using System;
using Microsoft.EntityFrameworkCore;
using Schemata.Abstractions.Entities;

namespace Schemata.Report.Integration.Tests.Fixtures;

[CanonicalName("source-records/{source_record}")]
[PrimaryKey(nameof(Uid))]
public sealed class SourceRecord : IIdentifier, ICanonicalName
{
    public Guid Uid { get; set; }

    public string? Name { get; set; }

    public string? CanonicalName { get; set; }

    public int Value { get; set; }
}
