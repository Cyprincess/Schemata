using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Identity.Foundation.Commands;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Messaging.Skeleton;
using Schemata.Abstractions.Advisors;

namespace Schemata.Identity.Foundation.Handlers;

internal sealed class ChangeUserPhoneHandler<TUser>(IdentityOperationHandler<TUser> operations)
    : IRequestHandler<ChangeUserPhoneRequest<TUser>, IdentityResult<Unit>>
    where TUser : SchemataUser, new()
{
    public Task<IdentityResult<Unit>> HandleAsync(
        ChangeUserPhoneRequest<TUser> request,
        AdviceContext                 context,
        CancellationToken             ct = default
    ) {
        if (request.Principal is null) {
            return Task.FromResult(IdentityResult<Unit>.Challenge());
        }

        return operations.ChangePhoneAsync(IdentityRequestHandler.Require(request).Request, request.Principal, context, ct);
    }
}