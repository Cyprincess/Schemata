using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Identity.Foundation.Commands;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Messaging.Skeleton;
using Schemata.Abstractions.Advisors;

namespace Schemata.Identity.Foundation.Handlers;

internal sealed class EnrollUserAuthenticatorHandler<TUser>(IdentityOperationHandler<TUser> operations)
    : IRequestHandler<EnrollUserAuthenticatorRequest<TUser>, IdentityResult<Unit>>
    where TUser : SchemataUser, new()
{
    public Task<IdentityResult<Unit>> HandleAsync(
        EnrollUserAuthenticatorRequest<TUser> request,
        AdviceContext                         context,
        CancellationToken                     ct = default
    ) {
        if (request.Principal is null) {
            return Task.FromResult(IdentityResult<Unit>.Challenge());
        }

        return operations.EnrollAsync(IdentityRequestHandler.Require(request).Request, request.Principal, context, ct);
    }
}