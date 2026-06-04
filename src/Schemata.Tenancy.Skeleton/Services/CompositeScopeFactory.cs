using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Tenancy.Skeleton.Services;

/// <summary>
///     <see cref="IServiceScopeFactory" /> that produces composite scopes — a fresh host scope
///     for Scoped/Transient resolution plus a stable reference back to the tenant override
///     container for Singleton resolution.
/// </summary>
internal sealed class CompositeScopeFactory : IServiceScopeFactory
{
    private readonly TenantCompositeServiceProvider _composite;

    public CompositeScopeFactory(TenantCompositeServiceProvider composite) { _composite = composite; }

    #region IServiceScopeFactory Members

    public IServiceScope CreateScope() {
        var hostFactory = _composite.Root.GetRequiredService<IServiceScopeFactory>();
        var hostScope   = hostFactory.CreateScope();
        return new CompositeScope(_composite, hostScope);
    }

    #endregion
}