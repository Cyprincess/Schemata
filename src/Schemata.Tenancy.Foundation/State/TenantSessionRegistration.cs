using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Session;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Schemata.Core.State;

namespace Schemata.Tenancy.Foundation.State;

/// <summary>
///     Registers the native session capability of one tenant profile inside the tenant
///     collection: closed <see cref="SessionOptions" /> services, the declared
///     <see cref="ISessionStore" />, and a fixed <see cref="DefaultStatePipelineProvider" />
///     over the tenant services, so <c>UseStatePipeline(Session)</c> builds the native
///     <c>SessionMiddleware</c> branch from this closure. The branch derives its session-id
///     cookie protector from the profile's fixed data protection provider, and its cookie is
///     named <c>CookiePrefix + SessionDefaults.CookieName</c>, so a session identifier issued
///     by another profile starts a fresh session here. Session state commits through the
///     asynchronous <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache" />
///     members of the tenant session cache, and synchronous session access requires a bounded
///     synchronous backend. The global session registration stays untouched; each request
///     resolves exactly one branch.
/// </summary>
internal static class TenantSessionRegistration
{
    /// <summary>Registers one tenant-owned session capability into the tenant collection.</summary>
    public static void Register<TStore>(IServiceCollection services, Action<SessionOptions> configure, TenantStateProfile profile)
        where TStore : class, ISessionStore {
        ClosedOptions.Register<SessionOptions>(services);

        // The closed options services above already satisfy AddOptions; the native Configure
        // extension would TryAdd open-generic options descriptors that tenant containers
        // reject. The framework name applies after the application callback.
        services.AddSingleton<IConfigureOptions<SessionOptions>>(new ConfigureNamedOptions<SessionOptions>(
            Options.DefaultName,
            options => {
                configure(options);
                options.Cookie.Name = profile.CookiePrefix + SessionDefaults.CookieName;
            }));

        services.TryAddTransient<ISessionStore, TStore>();
        services.AddStatePipelineProvider();
    }
}
