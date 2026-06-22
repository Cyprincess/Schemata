using System.Threading.Tasks;
using ProtoBuf.Grpc;
using Schemata.Abstractions.Resource;
using Schemata.Flow.Foundation;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Grpc.Services;

/// <summary>Lists registry-backed Flow process definitions for gRPC clients.</summary>
public sealed class ProcessDefinitionService(ProcessDefinitionQueryService query) : IProcessDefinitionService
{
    #region IProcessDefinitionService Members

    public ValueTask<ListResultBase<ProcessDefinitionInfo>> ListProcessDefinitionsAsync(
        ListRequest request,
        CallContext context = default
    ) {
        var entities = query.ListProcessDefinitions();
        return new(new ListResultBase<ProcessDefinitionInfo> { Entities = entities });
    }

    #endregion
}
