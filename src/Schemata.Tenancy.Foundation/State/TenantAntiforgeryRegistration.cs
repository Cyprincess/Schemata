using System;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using Schemata.Core.State;

namespace Schemata.Tenancy.Foundation.State;

/// <summary>
///     Registers the native antiforgery capability of one tenant profile inside the tenant
///     collection. The native service graph is composed on a standalone collection through the
///     public <c>AddAntiforgery</c> registration, then only the descriptors the capability
///     owns — the antiforgery contract surface (including the internal token contracts, carried
///     as service descriptors so no internal type is named here), its closed options
///     configurators, and the object-pool infrastructure — are transplanted with native
///     <c>TryAdd</c> semantics, so application registrations composed earlier in the tenant
///     state keep winning. The data protection graph the public registration adds to the
///     standalone collection is not transplanted: the fixed tenant provider registered by
///     <c>UseState</c> precedes this closure, so the transplanted token serializer derives its
///     protector from the profile's purpose chain. A fixed
///     <see cref="DefaultStatePipelineProvider" /> over the tenant services lets the
///     <c>UseStatePipeline(Antiforgery)</c> selection build the native middleware branch from
///     this graph.
/// </summary>
internal static class TenantAntiforgeryRegistration
{
    /// <summary>Registers one tenant-owned antiforgery capability into the tenant collection.</summary>
    /// <param name="services">The tenant service collection.</param>
    /// <param name="configure">The application antiforgery configuration.</param>
    /// <param name="profile">The tenant state profile the capability is bound to.</param>
    /// <param name="host">The host provider that must own the global antiforgery registration.</param>
    public static void Register(IServiceCollection services, Action<AntiforgeryOptions> configure, TenantStateProfile profile, IServiceProvider host) {
        ClosedOptions.Register<AntiforgeryOptions>(services);

        var native = new ServiceCollection();
        native.AddAntiforgery(configure);
        foreach (var descriptor in native) {
            if (!IsCapabilityOwned(descriptor.ServiceType)) continue;

            // This registration's application callback composes unconditionally; the native
            // default setup keeps its enumerable TryAdd semantics.
            if (descriptor.ImplementationInstance is not null) {
                services.Add(descriptor);
                continue;
            }

            if (descriptor.ServiceType == typeof(IConfigureOptions<AntiforgeryOptions>)) {
                services.TryAddEnumerable(descriptor);
                continue;
            }

            services.TryAdd(descriptor);
        }

        // Registered after the transplanted configurators, so the profile namespace applies to
        // the name the native setup derived from the application discriminator.
        services.AddSingleton<IConfigureOptions<AntiforgeryOptions>>(new ConfigureNamedOptions<AntiforgeryOptions>(
            Options.DefaultName,
            options => options.Cookie.Name = profile.CookiePrefix + (options.Cookie.Name ?? AntiforgeryOptions.DefaultCookiePrefix)));

        // The state branch is contributed once by the shared provider registration; this
        // capability's host boundary is enforced by a validator that runs when the provider is
        // created, independent of the order capabilities are declared in.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStatePipelineValidator>(new TenantAntiforgeryStateValidator(profile, host)));
        services.AddStatePipelineProvider();
    }

    private static bool IsCapabilityOwned(Type serviceType) {
        // The antiforgery contract surface includes internal token contracts (generator,
        // serializer, store, claim uid extractor); the namespace check transplants them as
        // service descriptors without naming internal types.
        if (serviceType.Namespace == typeof(IAntiforgery).Namespace) return true;
        if (serviceType == typeof(IConfigureOptions<AntiforgeryOptions>)) return true;
        if (serviceType == typeof(ObjectPoolProvider)) return true;
        return serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(ObjectPool<>);
    }
}
