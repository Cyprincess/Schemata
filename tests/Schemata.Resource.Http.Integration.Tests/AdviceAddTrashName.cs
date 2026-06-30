using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Resource.Http.Integration.Tests.Fixtures;

namespace Schemata.Resource.Http.Integration.Tests;

internal sealed class AdviceAddTrashName : IRepositoryAddAdvisor<Trash>
{
    #region IRepositoryAddAdvisor<Trash> Members

    public int Order => 0;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext      ctx,
        IRepository<Trash> repository,
        Trash              entity,
        CancellationToken  ct
    ) {
        if (string.IsNullOrWhiteSpace(entity.Name)) {
            entity.Name = $"trash-{Identifiers.NewUid():n}";
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
