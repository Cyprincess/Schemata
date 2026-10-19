using System;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Schemata.Tenancy.Foundation.State;

/// <summary>
///     Registers the native Identity bearer capability of one logical scheme inside a tenant
///     collection: the scheme with the native bearer-token handler, closed
///     <see cref="BearerTokenOptions" />, and the scheme's named protectors derived from the
///     profile's fixed data protection provider with the native purpose chain. A refresh token
///     issued by another profile never unprotects here, so the Identity refresh endpoint inside
///     the tenant closure rejects it as an unauthenticated credential.
/// </summary>
internal static class TenantBearerRegistration
{
    private static readonly Type HandlerType = typeof(BearerTokenOptions).Assembly.GetType(
        "Microsoft.AspNetCore.Authentication.BearerToken.BearerTokenHandler",
        throwOnError: true)!;

    /// <summary>Registers one tenant-owned bearer scheme into the tenant collection.</summary>
    public static void Register(
        IServiceCollection         services,
        string                     scheme,
        Action<BearerTokenOptions> configure
    ) {
        TenantAuthenticationRegistration.RegisterCore(services);
        // The native bearer handler is internal, so the scheme declares its handler type
        // directly instead of going through AddBearerToken, whose Configure call would add
        // open-generic options descriptors a tenant collection rejects.
        services.AddSingleton<IConfigureOptions<AuthenticationOptions>>(
            new ConfigureNamedOptions<AuthenticationOptions>(
                Options.DefaultName,
                options => options.AddScheme(scheme, builder => builder.HandlerType = HandlerType)));
        // The composite scope resolves a handler type registered by the host before falling back
        // to activation, so the tenant collection must own its handler to keep the tenant
        // options (and their profile-bound protectors) in effect.
        services.TryAddTransient(HandlerType);

        ClosedOptions.Register<BearerTokenOptions>(services);

        // Post-configuration runs after every configure stage in the composed options chain, so
        // the profile protectors win over any host-derived protector and the bearer closure
        // stays bound to the tenant's own key material.
        services.AddSingleton<IPostConfigureOptions<BearerTokenOptions>>(sp => {
            var provider = sp.GetRequiredService<IDataProtectionProvider>();
            void Apply(BearerTokenOptions options) {
                configure(options);
                options.BearerTokenProtector = new TicketDataFormat(provider.CreateProtector(
                    "Microsoft.AspNetCore.Authentication.BearerToken", scheme, "BearerToken"));
                options.RefreshTokenProtector = new TicketDataFormat(provider.CreateProtector(
                    "Microsoft.AspNetCore.Authentication.BearerToken", scheme, "RefreshToken"));
            }

            return new PostConfigureOptions<BearerTokenOptions>(scheme, Apply);
        });
    }
}
