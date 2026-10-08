using Microsoft.AspNetCore.Builder;
using Schemata.Push.Http.Controllers;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Extension methods registering the Push HTTP control endpoints.</summary>
public static class PushHttpServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the Push subscription and send controller. Endpoints require authentication;
    ///     verbs additionally require the conventional policies <c>push.subscriptions.create</c>,
    ///     <c>push.subscriptions.get</c>, <c>push.subscriptions.delete</c>, and the operator-only
    ///     <c>push.send</c>. Hosts configure these policies through the standard authorization
    ///     builder.
    /// </summary>
    public static IServiceCollection AddSchemataPushHttp(this IServiceCollection services) {
        services.AddControllers();
        services.AddSchemataApplicationPart<PushController>();
        services.AddSchemataJsonSerializer(_ => { }, mvc: true);
        services.AddSchemataJsonTraits();
        return services;
    }
}
