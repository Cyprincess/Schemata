using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Tenancy;

namespace Schemata.Tenancy.Foundation.Middlewares;

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
        var evaluator = inner.GetType() == typeof(PolicyEvaluator)
            ? new PolicyEvaluator(context.RequestServices.GetRequiredService<IAuthorizationService>())
            : inner;
        return await evaluator.AuthorizeAsync(policy, authenticationResult, context, resource);
    }
}

internal interface ITenantRequestBinding
{
    TenantIdentity Identity { get; }
    Task BindPrincipalAsync(HttpContext http);
}

public sealed class TenantExecutionMiddleware(RequestDelegate next)
{
    public async Task Invoke(HttpContext http) {
        using var frame = TenantContext.Enter(http.Features.Get<ITenantRequestBinding>()?.Identity ?? TenantContext.Current);
        await next(http);
    }
}
