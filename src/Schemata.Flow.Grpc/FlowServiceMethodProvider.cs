using Grpc.AspNetCore.Server.Model;
using Schemata.Flow.Grpc.Services;

namespace Schemata.Flow.Grpc;

internal sealed class FlowServiceMethodProvider : IServiceMethodProvider<ProcessDefinitionService>
{
    public void OnServiceMethodDiscovery(ServiceMethodProviderContext<ProcessDefinitionService> context) {
        context.AddUnaryMethod(FlowGrpcMethods.Definitions, [],
            static (service, request, call) => service.ListProcessDefinitionsAsync(request, new(service, call)).AsTask());
    }
}
