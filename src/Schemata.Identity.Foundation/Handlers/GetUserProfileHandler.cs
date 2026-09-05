using System.Threading;
using System.Threading.Tasks;
using Schemata.Identity.Foundation.Queries;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Claims;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Messaging.Skeleton;

namespace Schemata.Identity.Foundation.Handlers;

internal sealed class GetUserProfileHandler<TUser>(IdentityOperationHandler<TUser> operations)
    : IRequestHandler<GetUserProfileQuery<TUser>, IdentityResult<ClaimsStore>>
    where TUser : SchemataUser, new()
{
    public Task<IdentityResult<ClaimsStore>> HandleAsync(
        GetUserProfileQuery<TUser> request,
        CancellationToken          ct = default
    ) {
        if (request.Principal is null) {
            return Task.FromResult(IdentityResult<ClaimsStore>.Challenge());
        }

        return operations.ProfileAsync(request.Principal, ct);
    }
}