using System;
using System.Collections.Generic;
using System.Linq;
using Schemata.Quota.Skeleton;
using Schemata.Quota.Skeleton.Entities;

namespace Schemata.Quota.Http.Models;

public sealed class ReserveQuotaRequest
{
    public string OperationId { get; set; } = string.Empty;
    public string? RequestedSubject { get; set; }
    public List<QuotaChargeRequest> Charges { get; set; } = [];
    public DateTimeOffset? ExpiresAt { get; set; }
    public string? Scope { get; set; }
    public string? Tenant { get; set; }
}

public sealed class QuotaChargeRequest
{
    public required QuotaPolicy Policy { get; set; }
    public string Metric { get; set; } = string.Empty;
    public long Amount { get; set; }

    internal QuotaCharge ToCharge() => new(Policy, new(Metric), Amount);
}

public sealed class SettleQuotaRequest
{
    public Guid ReservationUid { get; set; }
    public string? Scope { get; set; }
    public string? Tenant { get; set; }
}

public sealed class QuotaReservationResponse
{
    public Guid ReservationUid { get; init; }
    public string Scope { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string OperationId { get; init; } = string.Empty;
    public QuotaReservationState State { get; init; }
    public DateTimeOffset? ExpireTime { get; init; }
    public IReadOnlyList<QuotaAccountResponse> Accounts { get; init; } = [];
    public TimeSpan? RetryAfter { get; init; }

    internal static QuotaReservationResponse From(QuotaReservationView view) => new() {
        ReservationUid = view.Uid,
        Scope = view.Scope.Value,
        Subject = view.Subject.Value,
        OperationId = view.OperationId,
        State = view.State,
        ExpireTime = view.ExpireTime,
        Accounts = view.Accounts.Select(QuotaAccountResponse.From).ToArray(),
        RetryAfter = view.RetryAfter,
    };
}

public sealed class QuotaAccountResponse
{
    public required QuotaPolicy Policy { get; init; }
    public string Metric { get; init; } = string.Empty;
    public long Committed { get; init; }
    public long Reserved { get; init; }
    public decimal Available { get; init; }

    internal static QuotaAccountResponse From(QuotaAccountView view) => new() {
        Policy = view.Policy,
        Metric = view.Metric.Value,
        Committed = view.Committed,
        Reserved = view.Reserved,
        Available = view.Available,
    };
}

public sealed class QuotaLedgerResponse
{
    public IReadOnlyList<QuotaLedgerEntryResponse> Entries { get; init; } = [];

    internal static QuotaLedgerResponse From(IEnumerable<SchemataQuotaLedgerEntry> entries) => new() {
        Entries = entries.Select(QuotaLedgerEntryResponse.From).ToArray(),
    };
}

public sealed class QuotaLedgerEntryResponse
{
    public Guid ReservationUid { get; init; }
    public Guid AccountUid { get; init; }
    public string OperationId { get; init; } = string.Empty;
    public QuotaSettlementKind Kind { get; init; }
    public long Amount { get; init; }
    public DateTime? CreateTime { get; init; }

    internal static QuotaLedgerEntryResponse From(SchemataQuotaLedgerEntry entry) => new() {
        ReservationUid = entry.ReservationUid,
        AccountUid = entry.AccountUid,
        OperationId = entry.OperationId,
        Kind = entry.Kind,
        Amount = entry.Amount,
        CreateTime = entry.CreateTime,
    };
}
