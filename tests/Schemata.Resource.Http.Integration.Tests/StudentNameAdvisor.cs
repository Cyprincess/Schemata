using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Resource.Http.Integration.Tests.Fixtures;

namespace Schemata.Resource.Http.Integration.Tests;

/// <summary>
///     Assigns a GUID-based canonical name to every new Student.
/// </summary>
internal sealed class StudentNameAdvisor : IRepositoryAddAdvisor<Student>
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
            entity.Name = $"student-{Guid.NewGuid():N}";
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
