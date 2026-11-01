using System;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Common;
using Schemata.Tenancy.Foundation;
using Schemata.Tenancy.Foundation.State;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Registers the per-tenant state profile, fixed protection capability, and browser cookie
///     namespace via <see cref="SchemataTenancyOptions" />.
/// </summary>
public static class TenantStateExtensions
{
    /// <summary>
    ///     Opts a tenant into explicit state composition. Each tenant container receives the
    ///     <see cref="TenantStateProfile" /> composed by <paramref name="configure" /> and a
    ///     fixed <see cref="IDataProtectionProvider" /> derived from the host cryptography
    ///     backend as <c>host.CreateProtector("Schemata.Tenancy").CreateProtector(uid:N)</c>;
    ///     the derived protector is itself an <see cref="IDataProtectionProvider" />, so it is
    ///     registered directly. The host registration stays untouched, and a host missing its
    ///     cryptography provider fails the tenant container build instead of falling back to an
    ///     implicit key ring.
    /// </summary>
    public static SchemataTenancyBuilder<TTenant> UseState<TTenant>(
        this SchemataTenancyBuilder<TTenant> builder,
        Action<TenantStateBuilder<TTenant>>  configure
    )
        where TTenant : SchemataTenant {
        if (configure is null) {
            throw new ArgumentNullException(nameof(configure));
        }

        builder.Services.Configure<SchemataTenancyOptions>(options => options.DynamicOverrides.Add((identifier, services, root) => {
            if (!Guid.TryParse(identifier, out var uid) || uid == Guid.Empty) {
                throw new InvalidOperationException($"Tenant state requires a non-empty verified tenant identifier; tenant '{identifier}' has none.");
            }
            var protector = root.GetRequiredService<IDataProtectionProvider>()
                                .CreateProtector("Schemata.Tenancy")
                                .CreateProtector(uid.ToString("N"));
            services.AddSingleton<IDataProtectionProvider>(protector);
            var state = new TenantStateBuilder<TTenant>(services, uid, root);
            configure(state);
            var profile = state.CreateProfile();
            services.AddSingleton(new BrowserCookieNames(profile.CookiePrefix));
            services.AddSingleton(profile);
        }));
        return builder;
    }
}
