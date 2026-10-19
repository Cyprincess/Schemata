using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Advisors;

namespace Schemata.Security.Skeleton.Advisors;

public sealed class AuthorizationPipelineAdvisor<TRequest, TResponse>(
    Func<TRequest, ResourceTarget> resolve,
    IPermissionResolver resolver,
    IPermissionMatcher matcher,
    IServiceProvider services
) : IRequestPipelineAdvisor<TRequest, TResponse>
    where TRequest : IRequest<TResponse>, IRequestPrincipal
{
    public int Order => SecurityOrders.Authorization;

    public async Task<TResponse> AdviseAsync(
        AdviceContext                      ctx,
        TRequest                           request,
        RequestHandlerContinuation<TResponse> next,
        CancellationToken                  ct = default
    ) {
        var target = resolve(request);
        if (target.Entity is null || AnonymousAccess.IsAnonymous(target.Entity, target.Operation)) {
            return await next(ct);
        }

        var permission = resolver.Resolve(target.Operation, target.Entity);
        var policy = services.GetKeyedService<IResourceTargetPolicy>(target.Entity);
        if (policy is not null) {
            if (await policy.EvaluateAsync(target, request.Principal, ct) == AccessDecision.Allowed) {
                return await next(ct);
            }
            throw PermissionProbe.Create(target.Operation, target.Entity, permission, target.Name);
        }
        if (request.Principal is not null && matcher.IsMatch(request.Principal, permission)) {
            return await next(ct);
        }

        // The denial names the target the caller actually addressed — from the typed resolver,
        // never from the request envelope impersonating its entity.
        throw PermissionProbe.Create(target.Operation, target.Entity, permission, target.Name);
    }
}
