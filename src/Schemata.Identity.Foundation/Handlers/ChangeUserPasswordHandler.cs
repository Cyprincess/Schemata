using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Identity.Foundation.Commands;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Messaging.Skeleton;

namespace Schemata.Identity.Foundation.Handlers;

internal sealed class ChangeUserPasswordHandler<TUser>(IdentityOperationHandler<TUser> operations)
    : IRequestHandler<ChangeUserPasswordRequest<TUser>, IdentityResult<Unit>>
    where TUser : SchemataUser, new()
{
    public Task<IdentityResult<Unit>> HandleAsync(
        ChangeUserPasswordRequest<TUser> request,
        CancellationToken                ct = default
    ) => operations.ChangePasswordAsync(IdentityRequestHandler.Require(request).Request, request.Principal!, ct);
}