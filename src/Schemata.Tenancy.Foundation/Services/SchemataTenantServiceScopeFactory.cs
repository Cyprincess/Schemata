using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Schemata.Tenancy.Skeleton.Tenancy;

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
    private readonly ITenantServiceProviderFactory<TTenant> _factory;
    private readonly IServiceProvider                       _root;

    /// <summary>Creates a scope factory that switches between root and tenant providers.</summary>
    public SchemataTenantServiceScopeFactory(
        IServiceProvider                       root,
        ITenantServiceProviderFactory<TTenant> factory
    ) {
        _root     = root;
        _factory  = factory;
    }

    #region ITenantServiceScopeFactory<TTenant> Members

    public async ValueTask<AsyncServiceScope> CreateAsync(TenantIdentity identity, CancellationToken ct = default) {
        if (identity.Uid is not { } uid) return _root.CreateAsyncScope();
        var lease = await _factory.CreateServiceProviderAsync(uid, ct);
        try {
            var inner = lease.Provider.CreateScope();
            return new(new LeasedTenantScope(inner, lease));
        } catch {
            await lease.DisposeAsync();
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
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;
            Exception? failure = null;
            try { _inner.Dispose(); }
            catch (Exception error) { failure = error; }
            try { _lease.Dispose(); }
            catch (Exception cleanup) {
                Interlocked.Exchange(ref _disposed, 2);
                failure = failure is null ? cleanup : new AggregateException(failure, cleanup);
            }
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        }

        public async ValueTask DisposeAsync() {
            var previous = Interlocked.Exchange(ref _disposed, 1);
            if (previous == 1) return;
            Exception? failure = null;
            if (previous == 0) {
                try {
                    if (_inner is IAsyncDisposable inner) await inner.DisposeAsync();
                    else _inner.Dispose();
                } catch (Exception error) { failure = error; }
            }
            try { await _lease.DisposeAsync(); }
            catch (Exception cleanup) { failure = failure is null ? cleanup : new AggregateException(failure, cleanup); }
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        }

        #endregion
    }

    #endregion
}
