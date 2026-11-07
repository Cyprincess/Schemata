using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Errors;
using Schemata.Entity.Repository;

namespace Schemata.Identity.Skeleton.Advisors;

/// <summary>Checks alias uniqueness after manager normalization within the pending write transaction.</summary>
public interface IIdentityAliasUniquenessAdvisor<TUser>
{
    int Order { get; }

    Task<AdviseResult> AdviseAsync(
        AdviceContext context,
        Operations operation,
        TUser user,
        IUnitOfWork transaction,
        IList<ErrorFieldViolation> errors,
        CancellationToken ct);
}
