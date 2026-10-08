using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Security.Foundation.Stores;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;

namespace Schemata.Security.Foundation.Extensions;

/// <summary>Registers the unified token stores over the concrete token entity.</summary>
public static class SecurityServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the repository and cache token stores over the concrete
    ///     <see cref="SchemataToken" /> entity: the stores as scoped services, the plain
    ///     <see cref="ITokenStore{SchemataToken}" /> slot (TryAdd-guarded) forwarding to the
    ///     repository store, the <see cref="KeyedService.AnyKey" /> slot forwarding to the
    ///     repository store, and the nonce/jti/rate-slot keyed slots forwarding to the cache
    ///     store. Keyed factories resolve the scoped concrete instance so one instance serves
    ///     every key per scope.
    /// </summary>
    /// <remarks>
    ///     Idempotent: the plain slot is TryAdd-guarded and the keyed descriptors overwrite to
    ///     equivalent factories on re-registration. Called by the security feature
    ///     registration; hosts rarely need this directly.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddTokenStores(this IServiceCollection services)
    {
        services.AddScoped<RepositoryTokenStore>();
        services.AddScoped<CacheTokenStore>();

        services.TryAddScoped<ITokenStore<SchemataToken>>(sp => sp.GetRequiredService<RepositoryTokenStore>());

        services.AddKeyedScoped<ITokenStore<SchemataToken>>(
            KeyedService.AnyKey, (sp, _) => sp.GetRequiredService<RepositoryTokenStore>());
        services.AddKeyedScoped<ITokenStore<SchemataToken>>(
            SecurityConstants.TokenTypes.Nonce, (sp, _) => sp.GetRequiredService<CacheTokenStore>());
        services.AddKeyedScoped<ITokenStore<SchemataToken>>(
            SecurityConstants.TokenTypes.Jti, (sp, _) => sp.GetRequiredService<CacheTokenStore>());
        services.AddKeyedScoped<ITokenStore<SchemataToken>>(
            SecurityConstants.TokenTypes.RateSlot, (sp, _) => sp.GetRequiredService<CacheTokenStore>());

        return services;
    }
}
