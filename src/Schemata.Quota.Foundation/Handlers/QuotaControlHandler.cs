using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Messaging.Skeleton;
using Schemata.Quota.Skeleton;
using Schemata.Quota.Skeleton.Control;
using Schemata.Quota.Skeleton.Entities;

namespace Schemata.Quota.Foundation.Handlers;

public sealed class QuotaControlHandler(
    IAuthorizationService authorization,
    IQuotaAccounting accounting,
    IQuotaSubjectResolver subjects,
    IQuotaScopeResolver? scopes = null)
    : IRequestHandler<ReserveQuotaControlRequest, QuotaReservationView>,
      IRequestHandler<CommitQuotaControlRequest, QuotaReservationView>,
      IRequestHandler<ReleaseQuotaControlRequest, QuotaReservationView>,
      IRequestHandler<ListQuotaLedgerControlRequest, IReadOnlyList<SchemataQuotaLedgerEntry>>
{
    public async Task<QuotaReservationView> HandleAsync(
        ReserveQuotaControlRequest request,
        AdviceContext context,
        CancellationToken ct = default
    ) {
        var boundary = await BoundaryAsync(request.Principal, QuotaPolicies.Reserve, request.RequestedSubject, ct);
        return await accounting.ReserveAsync(new(boundary.Scope, boundary.Subject, request.OperationId,
            request.Charges, request.ExpiresAt), transaction: null, ct);
    }

    public async Task<QuotaReservationView> HandleAsync(
        CommitQuotaControlRequest request,
        AdviceContext context,
        CancellationToken ct = default
    ) {
        var boundary = await BoundaryAsync(request.Principal, QuotaPolicies.Commit, null, ct);
        return await accounting.CommitAsync(new(boundary.Scope, boundary.Subject, request.ReservationUid),
            transaction: null, ct);
    }

    public async Task<QuotaReservationView> HandleAsync(
        ReleaseQuotaControlRequest request,
        AdviceContext context,
        CancellationToken ct = default
    ) {
        var boundary = await BoundaryAsync(request.Principal, QuotaPolicies.Release, null, ct);
        return await accounting.ReleaseAsync(new(boundary.Scope, boundary.Subject, request.ReservationUid),
            transaction: null, ct);
    }

    public async Task<IReadOnlyList<SchemataQuotaLedgerEntry>> HandleAsync(
        ListQuotaLedgerControlRequest request,
        AdviceContext context,
        CancellationToken ct = default
    ) {
        var boundary = await BoundaryAsync(request.Principal, QuotaPolicies.Ledger, request.RequestedSubject, ct);
        return await accounting.ListLedgerAsync(new(boundary.Scope, boundary.Subject, request.Metric, request.PageSize), ct);
    }

    private async Task<QuotaBoundary> BoundaryAsync(
        ClaimsPrincipal? principal,
        string policy,
        string? requestedSubject,
        CancellationToken ct
    ) {
        if (principal is null) throw new UnauthenticatedException();
        var result = await authorization.AuthorizeAsync(principal, policy);
        if (!result.Succeeded) throw new PermissionDeniedException();
        if (scopes is null) throw new FailedPreconditionException();
        var scope = await scopes.ResolveAsync(ct);
        if (string.IsNullOrWhiteSpace(scope.Value)) throw new FailedPreconditionException();
        var subject = await subjects.ResolveAsync(principal, requestedSubject, ct);
        return new(scope, subject);
    }

    private readonly record struct QuotaBoundary(QuotaScope Scope, QuotaSubject Subject);
}
