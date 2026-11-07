using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Schemata.Messaging.Skeleton;
using Schemata.Quota.Foundation.Handlers;
using Schemata.Quota.Http.Models;
using Schemata.Quota.Skeleton;
using Schemata.Quota.Skeleton.Control;
using Schemata.Quota.Skeleton.Entities;

namespace Schemata.Quota.Http.Controllers;

[ApiController]
[Authorize]
[Route("~/v1")]
public sealed class QuotaController(IRequestDispatcher dispatcher) : ControllerBase
{
    [HttpPost("quota:reserve")]
    [Authorize(Policy = QuotaPolicies.Reserve)]
    public async Task<ActionResult<QuotaReservationResponse>> Reserve(
        [FromBody] ReserveQuotaRequest request,
        CancellationToken ct
    ) {
        QuotaControlRequestGuard.Validate(request.Scope, request.Tenant);
        var control = new ReserveQuotaControlRequest(request.OperationId, request.RequestedSubject,
            request.Charges.ConvertAll(charge => charge.ToCharge()), request.ExpiresAt) { Principal = User };
        var result = await dispatcher.SendAsync<ReserveQuotaControlRequest, QuotaReservationView>(control, ct);
        return Ok(QuotaReservationResponse.From(result));
    }

    [HttpPost("quota:commit")]
    [Authorize(Policy = QuotaPolicies.Commit)]
    public async Task<ActionResult<QuotaReservationResponse>> Commit(
        [FromBody] SettleQuotaRequest request,
        CancellationToken ct
    ) {
        QuotaControlRequestGuard.Validate(request.Scope, request.Tenant);
        var control = new CommitQuotaControlRequest(request.ReservationUid) { Principal = User };
        var result = await dispatcher.SendAsync<CommitQuotaControlRequest, QuotaReservationView>(control, ct);
        return Ok(QuotaReservationResponse.From(result));
    }

    [HttpPost("quota:release")]
    [Authorize(Policy = QuotaPolicies.Release)]
    public async Task<ActionResult<QuotaReservationResponse>> Release(
        [FromBody] SettleQuotaRequest request,
        CancellationToken ct
    ) {
        QuotaControlRequestGuard.Validate(request.Scope, request.Tenant);
        var control = new ReleaseQuotaControlRequest(request.ReservationUid) { Principal = User };
        var result = await dispatcher.SendAsync<ReleaseQuotaControlRequest, QuotaReservationView>(control, ct);
        return Ok(QuotaReservationResponse.From(result));
    }

    [HttpGet("quota/ledger")]
    [Authorize(Policy = QuotaPolicies.Ledger)]
    public async Task<ActionResult<QuotaLedgerResponse>> Ledger(
        [FromQuery] string? metric,
        [FromQuery(Name = "page_size")] int pageSize = 100,
        [FromQuery(Name = "requested_subject")] string? requestedSubject = null,
        [FromQuery] string? scope = null,
        [FromQuery] string? tenant = null,
        CancellationToken ct = default
    ) {
        QuotaControlRequestGuard.Validate(scope, tenant);
        var control = new ListQuotaLedgerControlRequest(metric is null ? null : new QuotaMetric(metric),
            pageSize, requestedSubject) { Principal = User };
        var result = await dispatcher.SendAsync<ListQuotaLedgerControlRequest, IReadOnlyList<SchemataQuotaLedgerEntry>>(control, ct);
        return Ok(QuotaLedgerResponse.From(result));
    }
}
