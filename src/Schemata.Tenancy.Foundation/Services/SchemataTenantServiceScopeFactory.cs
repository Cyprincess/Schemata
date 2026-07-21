using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Services;

/// <summary>
///     Creates service scopes using the tenant-isolated service provider when a tenant is resolved,
///     or the root provider otherwise. Tenant scopes hold an <see cref="ITenantProviderLease" />
///     that is released on scope disposal so active scopes keep their cached provider.
/// </summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
public class SchemataTenantServiceScopeFactory<TTenant> : ITenantServiceScopeFactory<TTenant>
    where TTenant : SchemataTenant
{
    private readonly ITenantContextAccessor<TTenant>        _accessor;
    private readonly ITenantServiceProviderFactory<TTenant> _factory;
    private readonly IServiceProvider                       _root;

    /// <summary>Creates a scope factory that switches between root and tenant providers.</summary>
    public SchemataTenantServiceScopeFactory(
        IServiceProvider                       root,
        ITenantContextAccessor<TTenant>        accessor,
        ITenantServiceProviderFactory<TTenant> factory
    ) {
        _root     = root;
        _accessor = accessor;
        _factory  = factory;
    }

    #region ITenantServiceScopeFactory<TTenant> Members

    public IServiceScope CreateScope() {
        if (_accessor.Tenant is null) {
            if (_root is IServiceScope scope) {
                return scope;
            }

            return _root.CreateScope();
        }

        var lease = _factory.CreateServiceProvider(_accessor);
        try {
            var inner = lease.Provider.CreateScope();
            return new LeasedTenantScope(inner, lease);
        } catch {
            lease.Dispose();
            throw;
        }
    }

    #endregion

    #region Nested type: LeasedTenantScope

    private sealed class LeasedTenantScope : IServiceScope, IAsyncDisposable
    {
        private readonly IServiceScope        _inner;
        private readonly ITenantProviderLease _lease;
        private          int                  _disposed;

        public LeasedTenantScope(IServiceScope inner, ITenantProviderLease lease) {
            _inner = inner;
            _lease = lease;
        }

        #region IServiceScope Members

        public IServiceProvider ServiceProvider => _inner.ServiceProvider;

        public void Dispose() {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) {
                return;
            }

            try {
                _inner.Dispose();
            } finally {
                _lease.Dispose();
            }
        }

        public async ValueTask DisposeAsync() {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) {
                return;
            }

            try {
                if (_inner is IAsyncDisposable inner) {
                    await inner.DisposeAsync();
                } else {
                    _inner.Dispose();
                }
            } finally {
                if (_lease is IAsyncDisposable lease) {
                    await lease.DisposeAsync();
                } else {
                    _lease.Dispose();
                }
            }
        }

        #endregion
    }

    #endregion
}
