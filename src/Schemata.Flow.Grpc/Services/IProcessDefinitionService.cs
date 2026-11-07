using System.Threading.Tasks;
using ProtoBuf.Grpc;
using ProtoBuf.Grpc.Configuration;
using Schemata.Abstractions.Resource;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Grpc.Wire;

namespace Schemata.Flow.Grpc.Services;

/// <summary>Exposes Flow process definitions over the gRPC transport.</summary>
[Service(FlowGrpcMethods.ServiceName)]
public interface IProcessDefinitionService
{
    /// <summary>Lists registered Flow process definitions.</summary>
    [Operation]
    ValueTask<ListResultBase<ProcessDefinitionInfo, ProcessDefinitionInfo>> ListProcessDefinitionsAsync(
        ListProcessDefinitionsRequest request,
        CallContext context = default
    );
}
