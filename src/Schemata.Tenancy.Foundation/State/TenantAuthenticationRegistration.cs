using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Schemata.Core.State;

namespace Schemata.Tenancy.Foundation.State;

/// <summary>
///     The native authentication graph one tenant collection needs before any scheme can be
///     declared through a state capability: a keyed native <see cref="AuthenticationService" />
///     with tenant-owned scheme provider and handler provider, and closed
///     <see cref="AuthenticationOptions" />. The host scheme provider stays the default-scheme
///     authority because the tenant table is keyed, so an unnamed
///     <see cref="IAuthenticationSchemeProvider" /> resolution still reaches the host.
/// </summary>
internal static class TenantAuthenticationRegistration
{
    /// <summary>Key isolating the tenant scheme table from host default-scheme resolution.</summary>
    internal const string SchemeProviderKey = "Schemata.Tenancy.Authentication.Schemes";

    /// <summary>Key under which the tenant collection registers its native authentication service.</summary>
    internal const string ServiceKey = "Schemata.Tenancy.Authentication.Native";

    /// <summary>Registers the shared keyed authentication graph unless the collection has it.</summary>
    public static void RegisterCore(IServiceCollection services) {
        services.TryAddSingleton<IClaimsTransformation, NoopClaimsTransformation>();
        services.TryAddKeyedSingleton<IAuthenticationSchemeProvider>(SchemeProviderKey, (sp, _) => {
            var tenant = new AuthenticationSchemeProvider(sp.GetRequiredService<IOptions<AuthenticationOptions>>());
            var host   = sp.GetRequiredService<IAuthenticationSchemeProvider>();
            return new HostFallbackSchemeProvider(tenant, host);
        });
        services.TryAddScoped<IAuthenticationHandlerProvider>(sp => new ScopedAuthenticationHandlerProvider(
            sp, sp.GetRequiredKeyedService<IAuthenticationSchemeProvider>(SchemeProviderKey)));

        ClosedOptions.Register<AuthenticationOptions>(services);

        services.TryAddKeyedScoped<IAuthenticationService>(
            ServiceKey,
            (sp, _) => new AuthenticationService(
                sp.GetRequiredKeyedService<IAuthenticationSchemeProvider>(SchemeProviderKey),
                sp.GetRequiredService<IAuthenticationHandlerProvider>(),
                sp.GetRequiredService<IClaimsTransformation>(),
                sp.GetRequiredService<IOptions<AuthenticationOptions>>()));
    }

    /// <summary>Declares one scheme with the native handler in the tenant scheme table.</summary>
    public static void DeclareScheme<THandler>(IServiceCollection services, string scheme)
        where THandler : class, IAuthenticationHandler {
        services.AddSingleton<IConfigureOptions<AuthenticationOptions>>(
            new ConfigureNamedOptions<AuthenticationOptions>(
                Options.DefaultName, options => options.AddScheme<THandler>(scheme, null)));
        services.TryAddTransient<THandler>();
    }

    /// <summary>
    ///     Tenant scheme table with host fallback: tenant-declared schemes resolve from the
    ///     tenant options, everything else (host schemes, default scheme slots, request-handler
    ///     schemes) resolves from the host table so a tenant request keeps the host's
    ///     authentication surface intact.
    /// </summary>
    private sealed class HostFallbackSchemeProvider(
        IAuthenticationSchemeProvider tenant,
        IAuthenticationSchemeProvider host
    ) : IAuthenticationSchemeProvider
    {
        public void AddScheme(AuthenticationScheme scheme) { tenant.AddScheme(scheme); }

        public async Task<IEnumerable<AuthenticationScheme>> GetAllSchemesAsync() {
            var tenantSchemes = await tenant.GetAllSchemesAsync();
            var hostSchemes   = await host.GetAllSchemesAsync();
            return tenantSchemes.Concat(hostSchemes.Where(scheme => tenantSchemes.All(item => item.Name != scheme.Name)));
        }

        public async Task<AuthenticationScheme?> GetDefaultAuthenticateSchemeAsync() {
            return await tenant.GetDefaultAuthenticateSchemeAsync() ?? await host.GetDefaultAuthenticateSchemeAsync();
        }

        public async Task<AuthenticationScheme?> GetDefaultChallengeSchemeAsync() {
            return await tenant.GetDefaultChallengeSchemeAsync() ?? await host.GetDefaultChallengeSchemeAsync();
        }

        public async Task<AuthenticationScheme?> GetDefaultForbidSchemeAsync() {
            return await tenant.GetDefaultForbidSchemeAsync() ?? await host.GetDefaultForbidSchemeAsync();
        }

        public async Task<AuthenticationScheme?> GetDefaultSignInSchemeAsync() {
            return await tenant.GetDefaultSignInSchemeAsync() ?? await host.GetDefaultSignInSchemeAsync();
        }

        public async Task<AuthenticationScheme?> GetDefaultSignOutSchemeAsync() {
            return await tenant.GetDefaultSignOutSchemeAsync() ?? await host.GetDefaultSignOutSchemeAsync();
        }

        public Task<IEnumerable<AuthenticationScheme>> GetRequestHandlerSchemesAsync() {
            return host.GetRequestHandlerSchemesAsync();
        }

        public async Task<AuthenticationScheme?> GetSchemeAsync(string name) {
            return await tenant.GetSchemeAsync(name) ?? await host.GetSchemeAsync(name);
        }

        public void RemoveScheme(string name) { tenant.RemoveScheme(name); }

        public bool TryAddScheme(AuthenticationScheme scheme) { return tenant.TryAddScheme(scheme); }
    }
}
