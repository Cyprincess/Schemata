using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Schemata.Core.State;

namespace Schemata.Tenancy.Foundation.State;

/// <summary>
///     Registers the native cookie capability of one logical scheme inside a tenant
///     collection: a keyed native <see cref="AuthenticationService" /> with tenant-owned
///     scheme provider and handler provider, closed options services, and the scheme's
///     named <see cref="CookieAuthenticationOptions" />. The host scheme provider stays
///     the default-scheme authority because the tenant table is keyed, so an unnamed
///     <see cref="IAuthenticationSchemeProvider" /> resolution still reaches the host.
/// </summary>
internal static class TenantCookieRegistration
{
    /// <summary>Registers one tenant-owned cookie scheme into the tenant collection.</summary>
    public static void Register(
        IServiceCollection                  services,
        string                              scheme,
        Action<CookieAuthenticationOptions> configure,
        TenantStateProfile                  profile
    ) {
        TenantAuthenticationRegistration.RegisterCore(services);
        TenantAuthenticationRegistration.DeclareScheme<CookieAuthenticationHandler>(services, scheme);

        ClosedOptions.Register<CookieAuthenticationOptions>(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<CookieAuthenticationOptions>, PostConfigureCookieAuthenticationOptions>());
        services.AddSingleton<IValidateOptions<CookieAuthenticationOptions>>(
            new ValidateOptions<CookieAuthenticationOptions>(
                scheme,
                options => options.Cookie.Expiration == null,
                "Cookie.Expiration is ignored, use ExpireTimeSpan instead."));

        // The fixed profile protector must win the TicketDataFormat: the composite resolves
        // the host post-configure first, which would otherwise bind the format to the host
        // protector before the tenant post-configure runs. Deriving here with the native
        // purposes keeps the host post-configure a no-op for protection.
        services.AddSingleton<IConfigureOptions<CookieAuthenticationOptions>>(sp => {
            var provider = sp.GetRequiredService<IDataProtectionProvider>();
            void Apply(CookieAuthenticationOptions options) {
                configure(options);
                options.Cookie.Name            = profile.CookiePrefix + Uri.EscapeDataString(scheme);
                options.DataProtectionProvider = provider;
                options.TicketDataFormat       = new TicketDataFormat(provider.CreateProtector(
                    "Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationMiddleware",
                    scheme,
                    "v2"));
                options.CookieManager ??= new ChunkingCookieManager();
                if (options.SessionStore is { } store) {
                    options.SessionStore = new TenantTicketStore($"{profile.TenantUid:N}/", store);
                }
            }
            return new ConfigureNamedOptions<CookieAuthenticationOptions>(scheme, Apply);
        });
    }
}

/// <summary>
///     Decorates a user-supplied <see cref="ITicketStore" /> so every key passes through
///     the owning profile's namespace. Session identifiers bound to another profile never
///     resolve, so a store reference shared across profiles cannot restore another
///     profile's ticket.
/// </summary>
internal sealed class TenantTicketStore(string prefix, ITicketStore store) : ITicketStore
{
    public async Task<string> StoreAsync(AuthenticationTicket ticket) {
        return prefix + await store.StoreAsync(ticket);
    }

    public async Task<string> StoreAsync(AuthenticationTicket ticket, CancellationToken cancellationToken) {
        return prefix + await store.StoreAsync(ticket, cancellationToken);
    }

    public async Task<string> StoreAsync(AuthenticationTicket ticket, HttpContext httpContext, CancellationToken cancellationToken) {
        return prefix + await store.StoreAsync(ticket, httpContext, cancellationToken);
    }

    public async Task RenewAsync(string key, AuthenticationTicket ticket) {
        if (Bind(key) is not { } bound) return;
        await store.RenewAsync(bound, ticket);
    }

    public async Task RenewAsync(string key, AuthenticationTicket ticket, CancellationToken cancellationToken) {
        if (Bind(key) is not { } bound) return;
        await store.RenewAsync(bound, ticket, cancellationToken);
    }

    public async Task RenewAsync(string key, AuthenticationTicket ticket, HttpContext httpContext, CancellationToken cancellationToken) {
        if (Bind(key) is not { } bound) return;
        await store.RenewAsync(bound, ticket, httpContext, cancellationToken);
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key) {
        return Bind(key) is not { } bound ? null : await store.RetrieveAsync(bound);
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key, CancellationToken cancellationToken) {
        return Bind(key) is not { } bound ? null : await store.RetrieveAsync(bound, cancellationToken);
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key, HttpContext httpContext, CancellationToken cancellationToken) {
        return Bind(key) is not { } bound ? null : await store.RetrieveAsync(bound, httpContext, cancellationToken);
    }

    public async Task RemoveAsync(string key) {
        if (Bind(key) is not { } bound) return;
        await store.RemoveAsync(bound);
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken) {
        if (Bind(key) is not { } bound) return;
        await store.RemoveAsync(bound, cancellationToken);
    }

    public async Task RemoveAsync(string key, HttpContext httpContext, CancellationToken cancellationToken) {
        if (Bind(key) is not { } bound) return;
        await store.RemoveAsync(bound, httpContext, cancellationToken);
    }

    private string? Bind(string key) {
        return key.StartsWith(prefix, StringComparison.Ordinal) ? key[prefix.Length..] : null;
    }
}
