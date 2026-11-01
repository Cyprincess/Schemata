using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Identity.Skeleton.Entities;

namespace Schemata.Identity.Scim.Integration.Tests.Fixtures;

internal sealed class FailingMembershipAdvisor : IRepositoryAddAdvisor<SchemataUserRole>
{
    public const string User = "users/failing-member";

    public int Order => 0;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext                 ctx,
        IRepository<SchemataUserRole> repository,
        SchemataUserRole              entity,
        MutationOptions               options,
        CancellationToken             ct
    ) {
        if (string.Equals(entity.UserId, User, StringComparison.Ordinal)) {
            throw new InvalidOperationException("The membership write failed.");
        }

        return Task.FromResult(AdviseResult.Continue);
    }
}
