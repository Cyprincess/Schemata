using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Schemata.Abstractions;
using Schemata.Messaging.Skeleton;
using Schemata.Quota.Skeleton.Entities;

namespace Schemata.Quota.Skeleton.Control;

public sealed record ReserveQuotaControlRequest(
    string OperationId,
    string? RequestedSubject,
    IReadOnlyList<QuotaCharge> Charges,
    DateTimeOffset? ExpiresAt) : IRequest<QuotaReservationView>, IRequestPrincipal
{
    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; }
}

public sealed record CommitQuotaControlRequest(Guid ReservationUid)
    : IRequest<QuotaReservationView>, IRequestPrincipal
{
    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; }
}

public sealed record ReleaseQuotaControlRequest(Guid ReservationUid)
    : IRequest<QuotaReservationView>, IRequestPrincipal
{
    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; }
}

public sealed record ListQuotaLedgerControlRequest(QuotaMetric? Metric, int PageSize, string? RequestedSubject)
    : IRequest<IReadOnlyList<SchemataQuotaLedgerEntry>>, IRequestPrincipal
{
    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; }
}
