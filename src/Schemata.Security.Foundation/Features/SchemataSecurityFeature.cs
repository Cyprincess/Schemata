using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Security.Foundation.Extensions;
using Schemata.Security.Foundation.Services;
using Schemata.Security.Foundation.Stores;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Security.Foundation.Features;

/// <summary>
///     Registers default security providers for Schemata applications against a host-supplied
///     security entity type: options validation, permission and entitlement
///     providers, the password hashers (plain default plus keyed AnyKey forwarding for
///     algorithm-keyed resolution), the secret verifier, the security store, the unified
///     token stores, and the named HTTP client for URI material fetches.
/// </summary>
/// <typeparam name="TSecurity">Concrete security entity type, must derive from <see cref="SchemataSecurity" />.</typeparam>
public class SchemataSecurityFeature<TSecurity> : FeatureBase
    where TSecurity : SchemataSecurity, new()
{
    /// <summary>Default priority for security feature startup.</summary>
    public const int DefaultPriority = Orders.Extension;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        services.Configure(configurators.PopOrDefault<SchemataSecurityOptions>());

        services.TryAddScoped<IPermissionResolver, DefaultPermissionResolver>();
        services.TryAddScoped<IPermissionMatcher, DefaultPermissionMatcher>();
        services.TryAddScoped(typeof(IAccessProvider<,>), typeof(DefaultAccessProvider<,>));
        services.TryAddScoped(typeof(IEntitlementProvider<,>), typeof(DefaultEntitlementProvider<,>));

        services.TryAddScoped<IPasswordHasher<SchemataSecurity>, PasswordHasher<SchemataSecurity>>();
        // The verifier resolves hashers by algorithm key; AnyKey forwards to the plain default so
        // every key resolves, and hosts override individual algorithms (bcrypt, argon2id, …).
        services.AddKeyedScoped<IPasswordHasher<SchemataSecurity>>(
            KeyedService.AnyKey, (sp, _) => sp.GetRequiredService<IPasswordHasher<SchemataSecurity>>());
        services.TryAddScoped<ISecretVerifier, SecretVerifier>();
        services.TryAddScoped<ISecurityStore<TSecurity>, SecurityStore<TSecurity>>();

        services.AddTokenStores();
        services.AddHttpClient(SecurityKeyMaterialExtensions.HttpClientName)
                .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(10));
    }
}

/// <summary>Registers default security providers against the default <see cref="SchemataSecurity" /> entity.</summary>
public sealed class SchemataSecurityFeature : SchemataSecurityFeature<SchemataSecurity>
{
}
