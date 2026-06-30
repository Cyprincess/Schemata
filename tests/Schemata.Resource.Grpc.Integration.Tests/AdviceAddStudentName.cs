using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Resource.Grpc.Integration.Tests.Fixtures;

namespace Schemata.Resource.Grpc.Integration.Tests;

internal sealed class AdviceAddStudentName : IRepositoryAddAdvisor<Student>
{
    #region IRepositoryAddAdvisor<Student> Members

    public int Order => 0;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext        ctx,
        IRepository<Student> repository,
        Student              entity,
        CancellationToken    ct
    ) {
        if (string.IsNullOrWhiteSpace(entity.Name)) {
            entity.Name = Identifiers.NewUid().ToString("n");
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
