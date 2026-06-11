using System.Threading.Tasks;
using ProtoBuf.Grpc;
using ProtoBuf.Grpc.Configuration;
using Schemata.Abstractions.Resource;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Grpc.Services;

public interface IProcessDefinitionService
{
    [Operation]
    ValueTask<ListResultBase<ProcessDefinitionInfo>> ListProcessDefinitionsAsync(
        ListRequest request,
        CallContext context = default
    );
}
