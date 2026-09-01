using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ProtoBuf.Grpc;
using Schemata.Abstractions.Resource;
using Schemata.Flow.Foundation.Commands;
using Schemata.Flow.Skeleton.Models;
using Schemata.Messaging.Skeleton;

namespace Schemata.Flow.Grpc.Services;

/// <summary>Lists registry-backed Flow process definitions for gRPC clients.</summary>
public sealed class ProcessDefinitionService(IQueryDispatcher dispatcher) : IProcessDefinitionService
{
    #region IProcessDefinitionService Members

    public async ValueTask<ListResultBase<ProcessDefinitionInfo>> ListProcessDefinitionsAsync(
        ListRequest request,
        CallContext context = default
    ) {
        var results = await dispatcher.SendAsync<ListProcessDefinitionsQuery, IReadOnlyList<ProcessDefinitionInfo>>(
            new ListProcessDefinitionsQuery(), context.CancellationToken);
        return new ListResultBase<ProcessDefinitionInfo> { Entities = results.ToList() };
    }

    #endregion
}
