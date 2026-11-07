using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Resource;
using Schemata.Resource.Foundation.Advisors;

namespace Schemata.Resource.Http.Integration.Tests.Fixtures;

internal sealed class AdviceAddStudentName : IResourceCreateAdvisor<Student, Student, Student>
{
    public int Order => 0;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext                            ctx,
        Student                                  request,
        Student                                  entity,
        ClaimsPrincipal?                         principal,
        HandledResponse<CreateResultBase<Student>> handled,
        CancellationToken                        ct = default
    ) {
        if (string.IsNullOrWhiteSpace(entity.Name)) {
            entity.Name = $"student-{Guid.NewGuid():n}";
        }

        return Task.FromResult(AdviseResult.Continue);
    }
}
