using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Tenancy.Skeleton.Tenancy;

namespace Schemata.Tenancy.Foundation.Middlewares;

/// <summary>
///     Binds the tenant-resolved principal after authentication and runs authorization inside the
///     request's tenant frame. The framework evaluator arrives through
///     <see cref="SchemataAuthenticationFeature.PolicyEvaluatorServiceKey" />.
/// </summary>
internal sealed class TenantPolicyEvaluator(IPolicyEvaluator inner) : IPolicyEvaluator
{
    public async Task<AuthenticateResult> AuthenticateAsync(AuthorizationPolicy policy, HttpContext context) {
        var result = await inner.AuthenticateAsync(policy, context);
        if (context.Features.Get<ITenantRequestBinding>() is { } binding) await binding.BindPrincipalAsync(context);
        return result;
    }

    public async Task<PolicyAuthorizationResult> AuthorizeAsync(AuthorizationPolicy policy, AuthenticateResult authenticationResult,
        HttpContext context, object? resource) {
        using var frame = TenantContext.Enter(context.Features.Get<ITenantRequestBinding>()?.Identity ?? TenantContext.Current);
        // The pipeline publishes this evaluator from the application provider; authorization runs
        // in the request's tenant scope, so the framework evaluator is resolved per request.
        var evaluator = context.RequestServices.GetRequiredKeyedService<IPolicyEvaluator>(
            SchemataAuthenticationFeature.PolicyEvaluatorServiceKey);
        return await evaluator.AuthorizeAsync(policy, authenticationResult, context, resource);
    }
}

/// <summary>Composes <see cref="TenantPolicyEvaluator" /> over the framework policy evaluator.</summary>
internal sealed class TenantPolicyEvaluatorDecorator : IDecorator<IPolicyEvaluator>
{
    public IPolicyEvaluator Decorate(IPolicyEvaluator inner) { return new TenantPolicyEvaluator(inner); }
}

internal interface ITenantRequestBinding
{
    TenantIdentity Identity { get; }
    IServiceProvider Original { get; }
    Task BindPrincipalAsync(HttpContext http);
}

public sealed class TenantExecutionMiddleware(RequestDelegate next)
{
    public async Task Invoke(HttpContext http) {
        using var frame = TenantContext.Enter(http.Features.Get<ITenantRequestBinding>()?.Identity ?? TenantContext.Current);
        await next(http);
    }
}
