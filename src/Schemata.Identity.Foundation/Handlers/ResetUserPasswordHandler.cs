using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Identity.Foundation.Commands;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Messaging.Skeleton;

namespace Schemata.Identity.Foundation.Handlers;

internal sealed class ResetUserPasswordHandler<TUser>(IdentityOperationHandler<TUser> operations)
    : IRequestHandler<ResetUserPasswordRequest<TUser>, IdentityResult<Unit>>
    where TUser : SchemataUser, new()
{
    public Task<IdentityResult<Unit>> HandleAsync(
        ResetUserPasswordRequest<TUser> request,
        CancellationToken               ct = default
    ) {
        if (request.Principal is null) {
            return Task.FromResult(IdentityResult<Unit>.Challenge());
        }

        return operations.ResetAsync(IdentityRequestHandler.Require(request).Request, request.Principal, ct);
    }
}