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
    ///     Registers the repository and cache token stores over <typeparamref name="TToken" />: the
    ///     stores as scoped services, the plain <see cref="ITokenStore{TToken}" /> slot
    ///     (TryAdd-guarded) forwarding to the repository store, the <see cref="KeyedService.AnyKey" />
    ///     slot forwarding to the repository store, and the nonce/jti/rate-slot keyed slots
    ///     forwarding to the cache store. Keyed factories resolve the scoped concrete instance so
    ///     one instance serves every key per scope.
    /// </summary>
    /// <remarks>
    ///     Idempotent: the plain slot is TryAdd-guarded and the keyed descriptors overwrite to
    ///     equivalent factories on re-registration. Called by the security feature
    ///     registration; hosts rarely need this directly.
    /// </remarks>
    /// <typeparam name="TToken">Concrete token entity type, must derive from <see cref="SchemataToken" />.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddTokenStores<TToken>(this IServiceCollection services)
        where TToken : SchemataToken, new() {
        services.AddScoped<RepositoryTokenStore<TToken>>();
        services.AddScoped<CacheTokenStore<TToken>>();

        services.TryAddScoped<ITokenStore<TToken>>(sp => sp.GetRequiredService<RepositoryTokenStore<TToken>>());

        services.AddKeyedScoped<ITokenStore<TToken>>(
            KeyedService.AnyKey, (sp, _) => sp.GetRequiredService<RepositoryTokenStore<TToken>>());
        services.AddKeyedScoped<ITokenStore<TToken>>(
            SecurityConstants.TokenTypes.Nonce, (sp, _) => sp.GetRequiredService<CacheTokenStore<TToken>>());
        services.AddKeyedScoped<ITokenStore<TToken>>(
            SecurityConstants.TokenTypes.Jti, (sp, _) => sp.GetRequiredService<CacheTokenStore<TToken>>());
        services.AddKeyedScoped<ITokenStore<TToken>>(
            SecurityConstants.TokenTypes.RateSlot, (sp, _) => sp.GetRequiredService<CacheTokenStore<TToken>>());

        return services;
    }
}
