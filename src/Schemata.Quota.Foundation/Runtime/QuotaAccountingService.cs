using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Quota.Foundation.Jobs;
using Schemata.Quota.Skeleton;
using Schemata.Quota.Skeleton.Entities;
using Schemata.Scheduling.Skeleton;

namespace Schemata.Quota.Foundation.Runtime;

public sealed class QuotaAccountingService(
    IQuotaStore store,
    QuotaReservationSettlement settlement,
    IServiceProvider services,
    TimeProvider time,
    IQuotaScopeResolver? scopes = null) : IQuotaAccounting
{
    public async ValueTask<QuotaReservationView> ReserveAsync(
        QuotaReservationRequest request,
        IUnitOfWork? transaction = null,
        CancellationToken ct = default
    ) {
        var scope = await ResolveScopeAsync(request.Scope, ct);
        ValidateSubject(request.Subject);
        if (string.IsNullOrWhiteSpace(request.OperationId)) throw new InvalidArgumentException();
        var charges = Canonicalize(request.Charges, request.ExpiresAt);
        foreach (var charge in charges) services.GetRequiredKeyedService<IQuotaAlgorithm>(charge.Policy.Algorithm);
        var fingerprint = QuotaRequestFingerprint.Compute(scope, request.Subject, charges, request.ExpiresAt);

        var owned = transaction is null;
        var uow = transaction ?? store.Begin(IsolationLevel.Serializable);
        if (!owned) store.Join(uow);
        Exception? failure = null;
        try {
            var now = time.GetUtcNow();
            await SettleExpiredAsync(now, ct);
            var existing = await store.FindReservationAsync(scope, request.Subject, request.OperationId, ct);
            if (existing is not null) {
                if (!string.Equals(existing.PayloadFingerprint, fingerprint, StringComparison.Ordinal)) {
                    throw new AlreadyExistsException();
                }

                var view = await settlement.ViewAsync(existing, ct);
                if (owned) await uow.CommitAsync(ct);
                return view;
            }

            var nowAccounts = new List<QuotaAccountView>(charges.Count);
            var items = new List<SchemataQuotaReservationItem>(charges.Count);
            var rejected = new List<(QuotaCharge Charge, QuotaDecision Decision)>();
            foreach (var charge in charges) {
                var algorithm = services.GetRequiredKeyedService<IQuotaAlgorithm>(charge.Policy.Algorithm);
                var account = await store.FindAccountAsync(scope, request.Subject, charge.Metric,
                    charge.Policy.Name, charge.Policy.Version, ct);
                var isNew = account is null;
                account ??= NewAccount(scope, request.Subject, charge, now);
                var segments = await store.ListSegmentsAsync(account.Uid, ct);
                var allocationUid = Guid.NewGuid();
                var decision = algorithm.Evaluate(new(charge.Policy, charge.Metric, charge.Amount, account, segments, now, allocationUid));
                if (!decision.Allowed) {
                    rejected.Add((charge, decision));
                    continue;
                }

                account.Committed = decision.Committed;
                account.Reserved = decision.Reserved;
                account.Available = decision.Available;
                account.WindowStart = decision.WindowStart;
                if (isNew) {
                    await store.AddAccountAsync(account, ct);
                } else {
                    await store.SaveAccountAsync(account, ct);
                }

                await store.ReplaceSegmentsAsync(account.Uid, decision.Segments, ct);
                items.Add(new() {
                    Uid = allocationUid, AccountUid = account.Uid, Metric = charge.Metric.Value,
                    PolicyName = charge.Policy.Name, PolicyVersion = charge.Policy.Version, Amount = charge.Amount,
                    Algorithm = charge.Policy.Algorithm, Limit = charge.Policy.Limit,
                    PeriodTicks = charge.Policy.Period.Ticks, Burst = charge.Policy.Burst, WindowStart = decision.WindowStart,
                });
                nowAccounts.Add(new(charge.Policy, charge.Metric, account.Committed, account.Reserved, account.Available));
            }

            if (rejected.Count > 0) throw CapacityExceeded(scope, request.Subject, rejected);
            var reservation = new SchemataQuotaReservation {
                Uid = Guid.NewGuid(), Scope = scope.Value, Subject = request.Subject.Value,
                OperationId = request.OperationId, PayloadFingerprint = fingerprint, State = QuotaReservationState.Pending,
                ExpireTime = request.ExpiresAt?.UtcDateTime,
            };
            await store.AddReservationAsync(reservation, items, ct);
            if (request.ExpiresAt is { } expiry && services.GetService<IScheduler>() is { } scheduler) {
                uow.AddCommitSink(CommitOrders.Domain, async token => {
                    await scheduler.TriggerAsync<QuotaExpiryJob>(new() {
                        StartTime = expiry.UtcDateTime,
                        Variables = new Dictionary<string, string?> {
                            [QuotaConstants.Jobs.ReservationUidVariable] = reservation.Uid.ToString("n"),
                        },
                    }, token);
                });
            }

            if (owned) await uow.CommitAsync(ct);
            return new(reservation.Uid, scope, request.Subject, request.OperationId, reservation.State,
                request.ExpiresAt, nowAccounts, null);
        } catch (Exception error) {
            failure = error;
            if (!owned) {
                try {
                    await uow.RollbackAsync(CancellationToken.None);
                } catch (Exception cleanup) {
                    throw new AggregateException(error, cleanup);
                }
            }
            throw;
        } finally {
            if (owned) {
                try { await uow.DisposeAsync(); }
                catch (Exception cleanup) when (failure is not null) { throw new AggregateException(failure, cleanup); }
            }
        }
    }

    public ValueTask<QuotaReservationView> CommitAsync(
        QuotaSettlementRequest request,
        IUnitOfWork? transaction = null,
        CancellationToken ct = default
    ) {
        return SettleAsync(request, QuotaSettlementKind.Commit, transaction, ct);
    }

    public ValueTask<QuotaReservationView> ReleaseAsync(
        QuotaSettlementRequest request,
        IUnitOfWork? transaction = null,
        CancellationToken ct = default
    ) {
        return SettleAsync(request, QuotaSettlementKind.Release, transaction, ct);
    }

    public async ValueTask<QuotaReservationView> ExpireAsync(
        Guid reservationUid,
        IUnitOfWork? transaction = null,
        CancellationToken ct = default
    ) {
        if (reservationUid == Guid.Empty) throw new InvalidArgumentException();
        return await SettleCoreAsync(reservationUid, QuotaSettlementKind.Expire, transaction, ct);
    }

    public async ValueTask<IReadOnlyList<SchemataQuotaLedgerEntry>> ListLedgerAsync(
        QuotaLedgerQuery query,
        CancellationToken ct = default
    ) {
        if (string.IsNullOrWhiteSpace(query.Scope.Value) || string.IsNullOrWhiteSpace(query.Subject.Value)
         || query.Metric is { } metric && string.IsNullOrWhiteSpace(metric.Value)
         || query.PageSize is < 1 or > 200) {
            throw new InvalidArgumentException();
        }

        var entries = new List<SchemataQuotaLedgerEntry>();
        await foreach (var entry in store.ListLedgerAsync(query, ct)) entries.Add(entry);
        return entries;
    }

    private async ValueTask<QuotaReservationView> SettleAsync(
        QuotaSettlementRequest request,
        QuotaSettlementKind kind,
        IUnitOfWork? transaction,
        CancellationToken ct
    ) {
        var scope = await ResolveScopeAsync(request.Scope, ct);
        ValidateSubject(request.Subject);
        if (request.ReservationUid == Guid.Empty) throw new InvalidArgumentException();
        return await SettleCoreAsync(request.ReservationUid, kind, transaction, ct, scope, request.Subject);
    }

    private async Task<QuotaReservationView> SettleCoreAsync(
        Guid reservationUid,
        QuotaSettlementKind kind,
        IUnitOfWork? transaction,
        CancellationToken ct,
        QuotaScope? expectedScope = null,
        QuotaSubject? expectedSubject = null
    ) {
        Exception? failure = null;
        try {
            var owned = transaction is null;
            var uow = transaction ?? store.Begin(IsolationLevel.Serializable);
            if (!owned) store.Join(uow);
            try {
                var view = await settlement.SettleAsync(reservationUid, kind, ct, expectedScope, expectedSubject)
                        ?? throw new NotFoundException();
                if (owned) await uow.CommitAsync(ct);
                return view;
            } catch (Exception error) {
                failure = error;
                throw;
            } finally {
                if (owned) {
                    try { await uow.DisposeAsync(); }
                    catch (Exception cleanup) when (failure is not null) { throw new AggregateException(failure, cleanup); }
                }
            }
        } catch (AbortedException error) when (ReferenceEquals(error, failure)) {
            if (transaction is not null) {
                try {
                    await transaction.RollbackAsync(CancellationToken.None);
                } catch (Exception cleanup) {
                    throw new AggregateException(error, cleanup);
                }
                throw;
            }

            if (await ReadSettledAsync(reservationUid, ct) is { } view) return view;
            throw;
        }
    }

    private async Task<QuotaReservationView?> ReadSettledAsync(Guid reservationUid, CancellationToken ct) {
        await using var scope = services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var currentStore = provider.GetRequiredService<IQuotaStore>();
        var reservation = await currentStore.GetReservationAsync(reservationUid, ct);
        if (reservation is null || reservation.State == QuotaReservationState.Pending) return null;
        return await provider.GetRequiredService<QuotaReservationSettlement>().ViewAsync(reservation, ct);
    }

    private async Task SettleExpiredAsync(DateTimeOffset now, CancellationToken ct) {
        var expired = new List<SchemataQuotaReservation>();
        await foreach (var reservation in store.ListExpiredPendingAsync(now, ct)) expired.Add(reservation);
        foreach (var reservation in expired) await settlement.SettleAsync(reservation.Uid, QuotaSettlementKind.Expire, ct);
    }

    private async ValueTask<QuotaScope> ResolveScopeAsync(QuotaScope? requested, CancellationToken ct) {
        var scope = requested ?? (scopes is not null ? await scopes.ResolveAsync(ct) : null);
        if (scope is not { } resolved || string.IsNullOrWhiteSpace(resolved.Value)) throw new FailedPreconditionException();
        return resolved;
    }

    private static void ValidateSubject(QuotaSubject subject) {
        if (string.IsNullOrWhiteSpace(subject.Value)) throw new InvalidArgumentException();
    }

    private static IReadOnlyList<QuotaCharge> Canonicalize(IReadOnlyList<QuotaCharge> charges, DateTimeOffset? expiresAt) {
        if (charges is null || charges.Count == 0) throw new InvalidArgumentException();
        var unique = new Dictionary<(string Name, int Version, string Metric), QuotaCharge>();
        foreach (var charge in charges) {
            Validate(charge, expiresAt);
            var key = (charge.Policy.Name, charge.Policy.Version, charge.Metric.Value);
            if (!unique.TryGetValue(key, out var existing)) {
                unique.Add(key, charge);
                continue;
            }

            if (existing.Policy != charge.Policy) throw new InvalidArgumentException();
            unique[key] = existing with { Amount = checked(existing.Amount + charge.Amount) };
        }

        return unique.Values.OrderBy(charge => charge.Policy.Name, StringComparer.Ordinal)
                     .ThenBy(charge => charge.Policy.Version)
                     .ThenBy(charge => charge.Metric.Value, StringComparer.Ordinal).ToList();
    }

    private static void Validate(QuotaCharge charge, DateTimeOffset? expiresAt) {
        if (charge is null || charge.Policy is null
         || string.IsNullOrWhiteSpace(charge.Metric.Value)
         || string.IsNullOrWhiteSpace(charge.Policy.Name)
         || string.IsNullOrWhiteSpace(charge.Policy.Algorithm)
         || charge.Amount <= 0 || charge.Policy.Limit <= 0 || charge.Policy.Version <= 0 || charge.Policy.Burst <= 0) {
            throw new InvalidArgumentException();
        }

        if (charge.Policy.Algorithm == QuotaConstants.Algorithms.Concurrency) {
            if (expiresAt is null) throw new InvalidArgumentException();
        } else if (charge.Policy.Period <= TimeSpan.Zero) {
            throw new InvalidArgumentException();
        }
    }

    private static SchemataQuotaAccount NewAccount(
        QuotaScope scope,
        QuotaSubject subject,
        QuotaCharge charge,
        DateTimeOffset now
    ) {
        return new() {
            Uid = Guid.NewGuid(), Scope = scope.Value, Subject = subject.Value, Metric = charge.Metric.Value,
            PolicyName = charge.Policy.Name, PolicyVersion = charge.Policy.Version,
            Available = charge.Policy.Algorithm == QuotaConstants.Algorithms.TokenBucket ? charge.Policy.Burst : charge.Policy.Limit,
            WindowStart = now,
        };
    }

    private static QuotaExceededException CapacityExceeded(
        QuotaScope scope,
        QuotaSubject subject,
        IEnumerable<(QuotaCharge Charge, QuotaDecision Decision)> rejected
    ) {
        var violations = new List<QuotaViolation>();
        TimeSpan? retry = null;
        foreach (var (charge, decision) in rejected) {
            violations.Add(new() {
                Subject = $"{scope.Value}/{subject.Value}", QuotaMetric = charge.Metric.Value,
                QuotaId = $"{charge.Policy.Name}:{charge.Policy.Version}", QuotaValue = charge.Policy.Limit,
                Description = $"Requested {charge.Amount} against {charge.Policy.Limit}.",
            });
            if (decision.RetryAfter is { } delay && (retry is null || delay < retry)) retry = delay;
        }

        var error = new QuotaExceededException(violations);
        if (retry is { } value) (error.Details ??= []).Add(new RetryInfoDetail { RetryDelay = value < TimeSpan.Zero ? TimeSpan.Zero : value });
        return error;
    }
}
