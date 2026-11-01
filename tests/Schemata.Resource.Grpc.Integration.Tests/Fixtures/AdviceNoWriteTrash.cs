using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

namespace Schemata.Resource.Grpc.Integration.Tests.Fixtures;

internal sealed class AdviceNoWriteTrash : IRepositoryAddAdvisor<Trash>, IRepositoryUpdateAdvisor<Trash>
{
    public int Order => -1;

    public Task<AdviseResult> AdviseAsync(AdviceContext ctx, IRepository<Trash> repository, Trash entity, MutationOptions options, CancellationToken ct) {
        return Task.FromResult(entity.FullName == "NoWrite" ? AdviseResult.Handle : AdviseResult.Continue);
    }
}
