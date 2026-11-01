using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

// ReSharper disable once CheckNamespace
namespace Schemata.Tenancy.Foundation.State;

/// <summary>
///     Registers the native options services for one closed options type inside a tenant
///     collection. Tenant containers reject open-generic registrations, so the closed mirrors
///     of <c>AddOptions()</c> — same service types, same lifetimes — are declared per options
///     type, and the <see cref="IOptionsMonitorCache{TOptions}" /> instance fresh per tenant
///     collection keeps the named options cache owned by the tenant. Public
///     <c>Configure</c>/<c>PostConfigure</c> chains stay native: registrations flow into
///     <see cref="OptionsFactory{TOptions}" /> unchanged.
/// </summary>
internal static class ClosedOptions
{
    /// <summary>
    ///     Registers the closed native options services for <typeparamref name="TOptions" />
    ///     unless the collection already contains them.
    /// </summary>
    public static IServiceCollection Register<TOptions>(this IServiceCollection services)
        where TOptions : class, new() {
        services.TryAddSingleton<IOptions<TOptions>, OptionsManager<TOptions>>();
        services.TryAddScoped<IOptionsSnapshot<TOptions>, OptionsManager<TOptions>>();
        services.TryAddSingleton<IOptionsMonitor<TOptions>, OptionsMonitor<TOptions>>();
        services.TryAddTransient<IOptionsFactory<TOptions>, OptionsFactory<TOptions>>();
        services.TryAddSingleton<IOptionsMonitorCache<TOptions>, OptionsCache<TOptions>>();
        return services;
    }
}
