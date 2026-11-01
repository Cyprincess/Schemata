using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Identity.Foundation.Commands;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Messaging.Skeleton;
using Schemata.Abstractions.Advisors;

namespace Schemata.Identity.Foundation.Handlers;

internal sealed class RefreshUserHandler<TUser>(IdentityOperationHandler<TUser> operations)
    : IRequestHandler<RefreshUserRequest<TUser>, IdentityResult<ClaimsPrincipal>>
    where TUser : SchemataUser, new()
{
    public Task<IdentityResult<ClaimsPrincipal>> HandleAsync(
        RefreshUserRequest<TUser> request,
        AdviceContext             context,
        CancellationToken         ct = default
    ) {
        if (request.Principal is null) {
            return Task.FromResult(IdentityResult<ClaimsPrincipal>.Challenge());
        }

        return operations.RefreshAsync(IdentityRequestHandler.Require(request).Ticket, request.Principal, context, ct);
    }
}