using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Advisors;

namespace Schemata.Security.Skeleton.Advisors;

public sealed class AuthenticationPipelineAdvisor<TRequest, TResponse>(Func<TRequest, (string Operation, Type? Entity)> resolve)
    : IRequestPipelineAdvisor<TRequest, TResponse>
    where TRequest : IRequest<TResponse>, IRequestPrincipal
{
    public int Order => SecurityOrders.Authentication;

    public Task<TResponse> AdviseAsync(
        AdviceContext                      ctx,
        TRequest                           request,
        RequestHandlerContinuation<TResponse> next,
        CancellationToken                  ct = default
    ) {
        var (operation, entity) = resolve(request);
        if (entity is null || AnonymousAccess.IsAnonymous(entity, operation)) {
            return next(ct);
        }

        if (request.Principal?.Identity?.IsAuthenticated != true) {
            throw new UnauthenticatedException();
        }

        return next(ct);
    }
}
