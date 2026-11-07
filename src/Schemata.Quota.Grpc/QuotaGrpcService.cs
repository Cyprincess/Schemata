using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using ProtoBuf.Grpc;
using Schemata.Abstractions.Exceptions;
using Schemata.Messaging.Skeleton;
using Schemata.Quota.Foundation.Handlers;
using Schemata.Quota.Skeleton;
using Schemata.Quota.Skeleton.Control;
using Schemata.Quota.Skeleton.Entities;

namespace Schemata.Quota.Grpc;

[Authorize]
public sealed class QuotaGrpcService(IRequestDispatcher dispatcher) : IQuotaGrpcService
{
    public async ValueTask<QuotaReservationWire> ReserveAsync(ReserveQuotaCommand request, CallContext context = default) {
        QuotaControlRequestGuard.Validate(request.Scope, request.Tenant);
        var control = new ReserveQuotaControlRequest(request.OperationId, request.RequestedSubject,
            request.Charges.Select(charge => charge.ToCharge()).ToArray(),
            request.ExpiresAt is { } expiry ? new DateTimeOffset(expiry.ToUniversalTime()) : null) {
            Principal = Caller(context),
        };
        var result = await dispatcher.SendAsync<ReserveQuotaControlRequest, QuotaReservationView>(control,
            context.CancellationToken);
        return QuotaReservationWire.From(result);
    }

    public async ValueTask<QuotaReservationWire> CommitAsync(SettleQuotaCommand request, CallContext context = default) {
        QuotaControlRequestGuard.Validate(request.Scope, request.Tenant);
        var control = new CommitQuotaControlRequest(ReservationUid(request)) { Principal = Caller(context) };
        var result = await dispatcher.SendAsync<CommitQuotaControlRequest, QuotaReservationView>(control,
            context.CancellationToken);
        return QuotaReservationWire.From(result);
    }

    public async ValueTask<QuotaReservationWire> ReleaseAsync(SettleQuotaCommand request, CallContext context = default) {
        QuotaControlRequestGuard.Validate(request.Scope, request.Tenant);
        var control = new ReleaseQuotaControlRequest(ReservationUid(request)) { Principal = Caller(context) };
        var result = await dispatcher.SendAsync<ReleaseQuotaControlRequest, QuotaReservationView>(control,
            context.CancellationToken);
        return QuotaReservationWire.From(result);
    }

    public async ValueTask<QuotaLedgerWire> ListLedgerAsync(ListQuotaLedgerCommand request, CallContext context = default) {
        QuotaControlRequestGuard.Validate(request.Scope, request.Tenant);
        var control = new ListQuotaLedgerControlRequest(request.Metric is null ? null : new QuotaMetric(request.Metric),
            request.PageSize, request.RequestedSubject) { Principal = Caller(context) };
        var result = await dispatcher.SendAsync<ListQuotaLedgerControlRequest, IReadOnlyList<SchemataQuotaLedgerEntry>>(control,
            context.CancellationToken);
        return QuotaLedgerWire.From(result);
    }

    private static Guid ReservationUid(SettleQuotaCommand request) {
        return Guid.TryParse(request.ReservationUid, out var uid) ? uid : throw new InvalidArgumentException();
    }

    private static ClaimsPrincipal Caller(CallContext context) {
        return context.ServerCallContext is { } server
            ? server.GetHttpContext().User
            : throw new InvalidOperationException("The quota control service requires a server call context.");
    }
}
