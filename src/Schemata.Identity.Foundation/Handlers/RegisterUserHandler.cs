using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Identity.Foundation.Commands;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Messaging.Skeleton;

namespace Schemata.Identity.Foundation.Handlers;

internal sealed class RegisterUserHandler<TUser>(IdentityOperationHandler<TUser> operations)
    : IRequestHandler<RegisterUserRequest<TUser>, IdentityResult<ClaimsPrincipal>>
    where TUser : SchemataUser, new()
{
    public Task<IdentityResult<ClaimsPrincipal>> HandleAsync(
        RegisterUserRequest<TUser> request,
        CancellationToken          ct = default
    ) => operations.RegisterAsync(IdentityRequestHandler.Require(request).Request, request.Principal!, ct);
}