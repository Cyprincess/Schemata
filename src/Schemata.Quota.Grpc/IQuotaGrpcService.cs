using System.Threading.Tasks;
using ProtoBuf.Grpc;
using ProtoBuf.Grpc.Configuration;

namespace Schemata.Quota.Grpc;

[Service("schemata.quota.v1.QuotaControl")]
public interface IQuotaGrpcService
{
    [Operation]
    ValueTask<QuotaReservationWire> ReserveAsync(ReserveQuotaCommand request, CallContext context = default);

    [Operation]
    ValueTask<QuotaReservationWire> CommitAsync(SettleQuotaCommand request, CallContext context = default);

    [Operation]
    ValueTask<QuotaReservationWire> ReleaseAsync(SettleQuotaCommand request, CallContext context = default);

    [Operation]
    ValueTask<QuotaLedgerWire> ListLedgerAsync(ListQuotaLedgerCommand request, CallContext context = default);
}
