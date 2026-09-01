using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Flow.Integration.Tests.Resource.Fixtures;

namespace Schemata.Flow.Integration.Tests.Resource;

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
            entity.Name = $"student-{Guid.NewGuid():n}";
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
