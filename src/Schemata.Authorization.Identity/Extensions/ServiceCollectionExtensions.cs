using Schemata.Identity.Skeleton.Entities;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Identity;
using Schemata.Authorization.Identity.Advisors;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Services;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Extension methods bridging ASP.NET Core Identity to the Authorization subject pipeline.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the chosen Identity user type's subject provider and session integration:
    ///     the host session store, the sign-in observer, and the OP session and logout
    ///     authorities the observer composes onto.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemataIdentitySubjectProvider<TUser>(this IServiceCollection services)
        where TUser : SchemataUser {
        services.TryAddScoped<ISubjectProvider, IdentitySubjectProvider<TUser>>();
        services.AddHttpContextAccessor();
        services.TryAddScoped<IOpSessionService, DefaultOpSessionService>();
        services.TryAddScoped<IOpLogoutService, DefaultOpLogoutService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IOpSessionStore, IdentityHostSessionStore>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<Schemata.Identity.Skeleton.IHostSignInObserver, OpSessionIdentityObserver>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IClaimsAdvisor, AdviceClaimsSubject>());

        return services;
    }
}
