using System.Linq;
using System.Threading.Tasks;
using Humanizer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Http;

public static class HttpContextExtensions
{
    public static async Task<AuthorizationResult> AuthorizeAsync(
        this HttpContext        context,
        ResourcePolicyAttribute policy,
        ResourceAttribute       resource,
        string?                 operation) {
        var authorization = context.RequestServices.GetRequiredService<IAuthorizationService>();
        var provider      = context.RequestServices.GetRequiredService<IAuthorizationPolicyProvider>();

        var endpoint = await AuthorizationPolicy.CombineAsync(provider, [
            new AuthorizeAttribute {
                Policy                = policy.Policy,
                AuthenticationSchemes = policy.AuthenticationSchemes,
                Roles = policy.Roles?.Replace("{entity}", resource.Entity.Name.Kebaberize())
                              .Replace("{operation}", operation.Kebaberize()),
            },
        ]);

        if (endpoint is null) {
            return AuthorizationResult.Success();
        }

        var result = await authorization.AuthorizeAsync(context.User, context, endpoint);

        if (result is { Succeeded: true }) {
            return AuthorizationResult.Success();
        }

        throw new AuthorizationException();
    }
}
