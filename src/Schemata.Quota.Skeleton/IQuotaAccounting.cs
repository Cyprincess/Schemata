using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Entity.Repository;
using Schemata.Quota.Skeleton.Entities;

namespace Schemata.Quota.Skeleton;

public interface IQuotaAccounting
{
    ValueTask<QuotaReservationView> ReserveAsync(QuotaReservationRequest request, IUnitOfWork? transaction = null, CancellationToken ct = default);
    ValueTask<QuotaReservationView> CommitAsync(QuotaSettlementRequest request, IUnitOfWork? transaction = null, CancellationToken ct = default);
    ValueTask<QuotaReservationView> ReleaseAsync(QuotaSettlementRequest request, IUnitOfWork? transaction = null, CancellationToken ct = default);
    ValueTask<QuotaReservationView> ExpireAsync(Guid reservationUid, IUnitOfWork? transaction = null, CancellationToken ct = default);
    ValueTask<IReadOnlyList<SchemataQuotaLedgerEntry>> ListLedgerAsync(QuotaLedgerQuery query, CancellationToken ct = default);
}
