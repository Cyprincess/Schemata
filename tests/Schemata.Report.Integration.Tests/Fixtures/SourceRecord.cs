using System;
using Schemata.Abstractions.Entities;

namespace Schemata.Report.Integration.Tests.Fixtures;

[CanonicalName("source-records/{source_record}")]
[Microsoft.EntityFrameworkCore.PrimaryKey(nameof(Uid))]
public sealed class SourceRecord : IIdentifier, ICanonicalName
{
    public Guid Uid { get; set; }

    public string? Name { get; set; }

    public string? CanonicalName { get; set; }

    public int Value { get; set; }
}
