using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Entity.Repository;
using Schemata.Quota.Skeleton.Entities;

namespace Schemata.Quota.Skeleton;

public interface IQuotaStore
{
    IUnitOfWork Begin(IsolationLevel isolationLevel);
    void Join(IUnitOfWork transaction);
    ValueTask<SchemataQuotaAccount?> FindAccountAsync(QuotaScope scope, QuotaSubject subject, QuotaMetric metric, string policyName, int policyVersion, CancellationToken ct);
    ValueTask<IReadOnlyList<SchemataQuotaSegment>> ListSegmentsAsync(Guid accountUid, CancellationToken ct);
    ValueTask<SchemataQuotaReservation?> FindReservationAsync(QuotaScope scope, QuotaSubject subject, string operationId, CancellationToken ct);
    ValueTask<SchemataQuotaReservation?> GetReservationAsync(Guid reservationUid, CancellationToken ct);
    IAsyncEnumerable<SchemataQuotaReservation> ListExpiredPendingAsync(DateTimeOffset now, CancellationToken ct);
    IAsyncEnumerable<SchemataQuotaLedgerEntry> ListLedgerAsync(QuotaLedgerQuery query, CancellationToken ct);
    Task AddAccountAsync(SchemataQuotaAccount account, CancellationToken ct);
    Task SaveAccountAsync(SchemataQuotaAccount account, CancellationToken ct);
    Task ReplaceSegmentsAsync(Guid accountUid, IReadOnlyList<SchemataQuotaSegment> segments, CancellationToken ct);
    Task AddReservationAsync(SchemataQuotaReservation reservation, IReadOnlyList<SchemataQuotaReservationItem> items, CancellationToken ct);
    Task SaveReservationAsync(SchemataQuotaReservation reservation, CancellationToken ct);
    Task AddLedgerEntriesAsync(IReadOnlyList<SchemataQuotaLedgerEntry> entries, CancellationToken ct);
}
