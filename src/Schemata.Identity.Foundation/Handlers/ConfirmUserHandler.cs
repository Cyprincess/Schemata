using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Identity.Foundation.Commands;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Messaging.Skeleton;

namespace Schemata.Identity.Foundation.Handlers;

internal sealed class ConfirmUserHandler<TUser>(IdentityOperationHandler<TUser> operations)
    : IRequestHandler<ConfirmUserRequest<TUser>, IdentityResult<Unit>>
    where TUser : SchemataUser, new()
{
    public Task<IdentityResult<Unit>> HandleAsync(
        ConfirmUserRequest<TUser> request,
        CancellationToken         ct = default
    ) => operations.ConfirmAsync(IdentityRequestHandler.Require(request).Request, request.Principal!, ct);
}