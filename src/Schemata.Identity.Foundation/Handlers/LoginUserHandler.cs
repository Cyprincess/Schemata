using Schemata.Abstractions;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Identity.Foundation.Commands;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Messaging.Skeleton;

namespace Schemata.Identity.Foundation.Handlers;

internal sealed class LoginUserHandler<TUser>(SchemataSignInManager<TUser> sign)
    : IRequestHandler<LoginUserRequest<TUser>, IdentityResult<Unit>>
    where TUser : SchemataUser, new()
{
    public Task<IdentityResult<Unit>> HandleAsync(
        LoginUserRequest<TUser> request,
        CancellationToken       ct = default
    ) {
        if (request.Principal is null) {
            return Task.FromResult(IdentityResult<Unit>.Challenge());
        }

        return sign.LoginAsync(IdentityRequestHandler.Require(request).Request, request.Principal, ct);
    }
}