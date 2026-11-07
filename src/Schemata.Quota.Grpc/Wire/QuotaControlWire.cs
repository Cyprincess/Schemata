using System;
using System.Collections.Generic;
using System.Linq;
using ProtoBuf;
using Schemata.Quota.Skeleton;
using Schemata.Quota.Skeleton.Entities;

namespace Schemata.Quota.Grpc;

[ProtoContract]
public sealed class ReserveQuotaCommand
{
    [ProtoMember(1)]
    public string OperationId { get; set; } = string.Empty;

    [ProtoMember(2)]
    public string? RequestedSubject { get; set; }

    [ProtoMember(3)]
    public List<QuotaChargeWire> Charges { get; set; } = [];

    [ProtoMember(4)]
    public DateTime? ExpiresAt { get; set; }

    [ProtoMember(5)]
    public string? Scope { get; set; }

    [ProtoMember(6)]
    public string? Tenant { get; set; }
}

[ProtoContract]
public sealed class SettleQuotaCommand
{
    [ProtoMember(1)]
    public string ReservationUid { get; set; } = string.Empty;

    [ProtoMember(2)]
    public string? Scope { get; set; }

    [ProtoMember(3)]
    public string? Tenant { get; set; }
}

[ProtoContract]
public sealed class ListQuotaLedgerCommand
{
    [ProtoMember(1)]
    public string? Metric { get; set; }

    [ProtoMember(2)]
    public int PageSize { get; set; } = 100;

    [ProtoMember(3)]
    public string? RequestedSubject { get; set; }

    [ProtoMember(4)]
    public string? Scope { get; set; }

    [ProtoMember(5)]
    public string? Tenant { get; set; }
}

[ProtoContract]
public sealed class QuotaChargeWire
{
    [ProtoMember(1)]
    public QuotaPolicyWire Policy { get; set; } = new();

    [ProtoMember(2)]
    public string Metric { get; set; } = string.Empty;

    [ProtoMember(3)]
    public long Amount { get; set; }

    internal QuotaCharge ToCharge() => new(Policy.ToPolicy(), new(Metric), Amount);
}

[ProtoContract]
public sealed class QuotaPolicyWire
{
    [ProtoMember(1)]
    public string Name { get; set; } = string.Empty;

    [ProtoMember(2)]
    public int Version { get; set; }

    [ProtoMember(3)]
    public string Algorithm { get; set; } = string.Empty;

    [ProtoMember(4)]
    public long Limit { get; set; }

    [ProtoMember(5)]
    public TimeSpan Period { get; set; }

    [ProtoMember(6)]
    public long Burst { get; set; }

    internal static QuotaPolicyWire From(QuotaPolicy policy) => new() {
        Name = policy.Name,
        Version = policy.Version,
        Algorithm = policy.Algorithm,
        Limit = policy.Limit,
        Period = policy.Period,
        Burst = policy.Burst,
    };

    internal QuotaPolicy ToPolicy() => new(Name, Version, Algorithm, Limit, Period, Burst);
}

[ProtoContract]
public sealed class QuotaReservationWire
{
    [ProtoMember(1)]
    public string ReservationUid { get; set; } = string.Empty;

    [ProtoMember(2)]
    public string Scope { get; set; } = string.Empty;

    [ProtoMember(3)]
    public string Subject { get; set; } = string.Empty;

    [ProtoMember(4)]
    public string OperationId { get; set; } = string.Empty;

    [ProtoMember(5)]
    public QuotaReservationState State { get; set; }

    [ProtoMember(6)]
    public DateTime? ExpireTime { get; set; }

    [ProtoMember(7)]
    public List<QuotaAccountWire> Accounts { get; set; } = [];

    [ProtoMember(8)]
    public TimeSpan? RetryAfter { get; set; }

    internal static QuotaReservationWire From(QuotaReservationView view) => new() {
        ReservationUid = view.Uid.ToString(),
        Scope = view.Scope.Value,
        Subject = view.Subject.Value,
        OperationId = view.OperationId,
        State = view.State,
        ExpireTime = view.ExpireTime?.UtcDateTime,
        Accounts = view.Accounts.Select(QuotaAccountWire.From).ToList(),
        RetryAfter = view.RetryAfter,
    };
}

[ProtoContract]
public sealed class QuotaAccountWire
{
    [ProtoMember(1)]
    public QuotaPolicyWire Policy { get; set; } = new();

    [ProtoMember(2)]
    public string Metric { get; set; } = string.Empty;

    [ProtoMember(3)]
    public long Committed { get; set; }

    [ProtoMember(4)]
    public long Reserved { get; set; }

    [ProtoMember(5)]
    public decimal Available { get; set; }

    internal static QuotaAccountWire From(QuotaAccountView view) => new() {
        Policy = QuotaPolicyWire.From(view.Policy),
        Metric = view.Metric.Value,
        Committed = view.Committed,
        Reserved = view.Reserved,
        Available = view.Available,
    };
}

[ProtoContract]
public sealed class QuotaLedgerWire
{
    [ProtoMember(1)]
    public List<QuotaLedgerEntryWire> Entries { get; set; } = [];

    internal static QuotaLedgerWire From(IEnumerable<SchemataQuotaLedgerEntry> entries) => new() {
        Entries = entries.Select(QuotaLedgerEntryWire.From).ToList(),
    };
}

[ProtoContract]
public sealed class QuotaLedgerEntryWire
{
    [ProtoMember(1)]
    public string ReservationUid { get; set; } = string.Empty;

    [ProtoMember(2)]
    public string AccountUid { get; set; } = string.Empty;

    [ProtoMember(3)]
    public string OperationId { get; set; } = string.Empty;

    [ProtoMember(4)]
    public QuotaSettlementKind Kind { get; set; }

    [ProtoMember(5)]
    public long Amount { get; set; }

    [ProtoMember(6)]
    public DateTime? CreateTime { get; set; }

    internal static QuotaLedgerEntryWire From(SchemataQuotaLedgerEntry entry) => new() {
        ReservationUid = entry.ReservationUid.ToString(),
        AccountUid = entry.AccountUid.ToString(),
        OperationId = entry.OperationId,
        Kind = entry.Kind,
        Amount = entry.Amount,
        CreateTime = entry.CreateTime,
    };
}
