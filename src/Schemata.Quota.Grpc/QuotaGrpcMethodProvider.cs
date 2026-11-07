using Grpc.AspNetCore.Server.Model;
using Microsoft.AspNetCore.Authorization;
using Schemata.Quota.Skeleton.Control;

namespace Schemata.Quota.Grpc;

internal sealed class QuotaGrpcMethodProvider(QuotaGrpcModel model) : IServiceMethodProvider<QuotaGrpcService>
{
    public void OnServiceMethodDiscovery(ServiceMethodProviderContext<QuotaGrpcService> context) {
        context.AddUnaryMethod(model.Reserve, Metadata(QuotaPolicies.Reserve),
            static (service, request, call) => service.ReserveAsync(request, new(service, call)).AsTask());
        context.AddUnaryMethod(model.Commit, Metadata(QuotaPolicies.Commit),
            static (service, request, call) => service.CommitAsync(request, new(service, call)).AsTask());
        context.AddUnaryMethod(model.Release, Metadata(QuotaPolicies.Release),
            static (service, request, call) => service.ReleaseAsync(request, new(service, call)).AsTask());
        context.AddUnaryMethod(model.ListLedger, Metadata(QuotaPolicies.Ledger),
            static (service, request, call) => service.ListLedgerAsync(request, new(service, call)).AsTask());
        return;

        static object[] Metadata(string policy) => [new AuthorizeAttribute(), new AuthorizeAttribute { Policy = policy }];
    }
}
