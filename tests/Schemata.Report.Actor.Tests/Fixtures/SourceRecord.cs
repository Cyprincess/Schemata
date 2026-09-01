using System;
using Schemata.Abstractions.Entities;

namespace Schemata.Report.Actor.Tests.Fixtures;

/// <summary>Minimal repository-source entity the report plan reads through the Insight repository driver.</summary>
[CanonicalName("source-records/{source_record}")]
[Microsoft.EntityFrameworkCore.PrimaryKey(nameof(Uid))]
public sealed class SourceRecord : IIdentifier, ICanonicalName
{
    public Guid Uid { get; set; }

    public string? Name { get; set; }

    public string? CanonicalName { get; set; }

    public int Value { get; set; }
}