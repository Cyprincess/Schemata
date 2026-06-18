using System.Linq;
using System.Threading.Tasks;
using ProtoBuf.Grpc;
using Schemata.Abstractions.Resource;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Grpc.Services;

public sealed class ProcessDefinitionService(IProcessRegistry registry) : IProcessDefinitionService
{
    #region IProcessDefinitionService Members

    public ValueTask<ListResultBase<ProcessDefinitionInfo>> ListProcessDefinitionsAsync(
        ListRequest request,
        CallContext context = default
    ) {
        var entities = registry.GetRegisteredProcesses()
                               .Select(n => {
                                   var definition = registry.GetRegistration(n)?.Definition;
                                   return new ProcessDefinitionInfo {
                                       CanonicalName = $"definitions/{n}",
                                       DisplayName   = definition?.DisplayName,
                                       Description   = definition?.Description,
                                   };
                               })
                               .ToList();
        return new(new ListResultBase<ProcessDefinitionInfo> { Entities = entities });
    }

    #endregion
}
