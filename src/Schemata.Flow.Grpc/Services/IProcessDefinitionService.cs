using System.Threading.Tasks;
using ProtoBuf.Grpc;
using ProtoBuf.Grpc.Configuration;
using Schemata.Abstractions.Resource;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Grpc.Services;

/// <summary>Exposes Flow process definitions over the gRPC transport.</summary>
public interface IProcessDefinitionService
{
    /// <summary>Lists registered Flow process definitions.</summary>
    [Operation]
    ValueTask<ListResultBase<ProcessDefinitionInfo>> ListProcessDefinitionsAsync(
        ListRequest request,
        CallContext context = default
    );
}
