using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Advisors;

namespace Schemata.Security.Skeleton.Advisors;

public sealed class AuthorizationPipelineAdvisor<TRequest, TResponse>(
    Func<TRequest, (string Operation, Type? Entity)> resolve,
    IPermissionResolver resolver,
    IPermissionMatcher matcher
) : IRequestPipelineAdvisor<TRequest, TResponse>
    where TRequest : IRequest<TResponse>, IRequestPrincipal
{
    public int Order => SecurityOrders.Authorization;

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

        var permission = resolver.Resolve(operation, entity);
        if (request.Principal is not null && matcher.IsMatch(request.Principal, permission)) {
            return next(ct);
        }

        throw PermissionProbe.Create(operation, entity, resolver, matcher, request.Principal);
    }
}
