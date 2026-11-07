using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Tenancy.Foundation.State;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers tenant-owned native state branch construction.</summary>
public static class TenantStateServiceCollectionExtensions
{
    /// <summary>Registers one provider over the collection and runs its validators before creation.</summary>
    public static IServiceCollection AddTenantStatePipelineProvider(this IServiceCollection services) {
        services.TryAddSingleton<ITenantStatePipelineProvider>(sp => {
            foreach (var validator in sp.GetServices<ITenantStatePipelineValidator>()) {
                validator.Validate();
            }

            return new DefaultTenantStatePipelineProvider(sp);
        });

        return services;
    }
}
