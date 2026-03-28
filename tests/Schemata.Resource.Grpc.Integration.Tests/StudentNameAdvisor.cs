using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Resource.Grpc.Integration.Tests.Fixtures;

namespace Schemata.Resource.Grpc.Integration.Tests;

/// <summary>
///     Assigns a canonical name to every new Student so that GetByCanonicalNameAsync works.
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
            // Set just the leaf slug; AdviceAddCanonicalName will resolve the full
            // canonical name "students/{slug}" using the [CanonicalName] attribute.
            entity.Name = Guid.NewGuid().ToString("N");
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
