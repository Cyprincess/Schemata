using System;
using System.Collections.Generic;

namespace Schemata.Report.Integration.Tests.Fixtures;

public sealed class ValueQuery
{
    public ulong Unsigned { get; set; }
    public decimal Precise { get; set; }
    public decimal? Missing { get; set; }
    public List<decimal?> Sequence { get; set; } = [];
    public Dictionary<string, decimal?> Map { get; set; } = [];
    public int[] EmptyList { get; set; } = [];
    public Dictionary<string, int> EmptyMap { get; set; } = [];
    public DateTimeOffset Offset { get; set; }
    public byte[] Blob { get; set; } = [0, 255];
    public DateTime Timestamp { get; set; } = new(638999999999999999, DateTimeKind.Utc);
    public Guid Identifier { get; set; } = Guid.Parse("3f0bafc4-298d-4521-a302-8d646c81a515");
    public TimeSpan Duration { get; set; } = TimeSpan.FromTicks(-1234567890123);
    public char Character { get; set; } = 'λ';
    public bool Flag { get; set; }
    public double Number { get; set; } = 1.25;
    public int Id { get; set; }
    public List<PrecisionChild> Children { get; set; } = [];
}

public sealed class PrecisionChild
{
    public decimal Amount { get; set; }
    public int Number { get; set; }
}
