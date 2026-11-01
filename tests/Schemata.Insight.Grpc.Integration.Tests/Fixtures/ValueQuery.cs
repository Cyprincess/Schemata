using System;
using System.Collections.Generic;

namespace Schemata.Insight.Grpc.Integration.Tests.Fixtures;

public sealed class ValueQuery
{
    public int Id { get; init; }
    public ulong Unsigned { get; init; }
    public decimal Precise { get; init; }
    public byte[] Blob { get; init; } = [];
    public Guid Identifier { get; init; }
    public DateTime Timestamp { get; init; }
    public DateTimeOffset Offset { get; init; }
    public TimeSpan Duration { get; init; }
    public ValueKind Kind { get; init; }
    public char Character { get; init; }
    public bool Flag { get; init; }
    public double Number { get; init; }
    public long Signed { get; init; }
    public uint Positive { get; init; }
    public sbyte SmallSigned { get; init; }
    public ushort SmallUnsigned { get; init; }
    public short Short { get; init; }
    public byte Byte { get; init; }
    public float Float { get; init; }
    public string? Missing { get; init; }
    public Dictionary<string, List<decimal?>> Amounts { get; init; } = [];
    public List<List<ulong?>> Sequences { get; init; } = [];
    public List<string> EmptyList { get; init; } = [];
    public Dictionary<string, decimal> EmptyMap { get; init; } = [];

    public static ValueQuery From(int id) => new() {
        Id = id,
        Unsigned = id == 1 ? ulong.MaxValue : (ulong)long.MaxValue,
        Precise = 7922816251426433759354395033.5m,
        Blob = [0, 1, 127, 255],
        Identifier = Guid.Parse("3f0bafc4-298d-4521-a302-8d646c81a515"),
        Timestamp = new DateTime(638999999999999999, DateTimeKind.Utc),
        Offset = new DateTimeOffset(638999999999999999, TimeSpan.FromHours(5.5)),
        Duration = TimeSpan.FromTicks(-1234567890123),
        Kind = ValueKind.Ready,
        Character = 'λ',
        Flag = true,
        Number = 1.25,
        Signed = long.MinValue,
        Positive = uint.MaxValue,
        SmallSigned = sbyte.MinValue,
        SmallUnsigned = ushort.MaxValue,
        Short = short.MinValue,
        Byte = byte.MaxValue,
        Float = -1.5f,
        Amounts = new() { ["exact"] = [null, 7922816251426433759354395033.5m] },
        Sequences = [[null, ulong.MaxValue], [(ulong)long.MaxValue]],
    };
}

public enum ValueKind { Ready, Complete }
