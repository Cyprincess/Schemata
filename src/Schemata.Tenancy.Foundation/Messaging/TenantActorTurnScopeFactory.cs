using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Actor.Skeleton;
using Schemata.Messaging.Skeleton;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Messaging;

/// <summary>
///     Two-phase <see cref="IActorTurnScopeFactory" /> for multi-tenant hosts. A scope cannot be
///     retargeted to another provider once created, so tenant identity must be resolved
///     <em>before</em> the turn's real scope is built — the same ordering the HTTP request pipeline
///     already follows (resolve the tenant, then install the tenant-aware scope factory).
/// </summary>
/// <remarks>
///     <para>
///         Phase 1: a short-lived bootstrap scope is created from the host root, and the
///         <see cref="TenantMessageContextPropagator{TTenant}" /> resolved from it restores tenant
///         identity directly — not the full registered <see cref="IMessageContextPropagator" />
///         collection, since this scope is discarded the moment phase 2 builds the real turn scope
///         and running unrelated propagators against a provider the turn itself never uses would be
///         wasted (or, worse, incorrect) work. This reloads the tenant carried in
///         <see cref="MessageContext.Items" /> and initializes
///         <see cref="ITenantContextInitializer{TTenant}" /> against this scope's own
///         <see cref="ITenantContextAccessor{TTenant}" /> instance.
///     </para>
///     <para>
///         Phase 2: <see cref="ITenantServiceScopeFactory{TTenant}" /> is resolved from that same
///         bootstrap scope and builds the real turn scope from the tenant-isolated provider — it
///         reads the accessor phase 1 just populated, and owns acquiring and eventually releasing
///         the <see cref="ITenantProviderLease" />. Every registered propagator restores in this
///         final scope — this is the turn's actual, ordinary propagator restoration, since the
///         final scope descends from a different provider than the bootstrap scope and has its own
///         accessor instance.
///     </para>
///     <para>
///         Registered with <c>Replace</c> over the default (host-root)
///         <see cref="IActorTurnScopeFactory" />. Disposal releases the final scope first, then the
///         bootstrap scope, even if releasing the final scope throws.
///     </para>
/// </remarks>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
public sealed class TenantActorTurnScopeFactory<TTenant>(IServiceScopeFactory scopeFactory) : IActorTurnScopeFactory
    where TTenant : SchemataTenant
{
    private static readonly IReadOnlyDictionary<string, string?> EmptyItems = new Dictionary<string, string?>();

    #region IActorTurnScopeFactory Members

    public async ValueTask<AsyncServiceScope> CreateAsync(MessageContext? context, CancellationToken ct = default) {
        var items     = context?.Items ?? EmptyItems;
        var bootstrap = scopeFactory.CreateAsyncScope();
        try {
            // Phase 1: tenant identity only (not the propagator collection - see the class remarks).
            // Resolved through DI, not constructed directly, so the composition root's own
            // registration (SchemataTenancyFeature) stays the single source of truth for how this
            // type is built - it would otherwise be silently bypassed if that registration ever
            // grew a constructor dependency.
            var identityPropagator = bootstrap.ServiceProvider.GetRequiredService<TenantMessageContextPropagator<TTenant>>();
            await identityPropagator.RestoreAsync(items, bootstrap.ServiceProvider, ct);

            var tenantScopeFactory = bootstrap.ServiceProvider.GetRequiredService<ITenantServiceScopeFactory<TTenant>>();
            var final               = tenantScopeFactory.CreateAsyncScope();
            try {
                // Phase 2: the turn's real, ordinary propagator restoration.
                foreach (var propagator in final.ServiceProvider.GetServices<IMessageContextPropagator>()) {
                    await propagator.RestoreAsync(items, final.ServiceProvider, ct);
                }

                return new AsyncServiceScope(new TwoPhaseScope(final, bootstrap));
            } catch {
                // The final scope was already allocated; release it before the bootstrap scope so
                // its ITenantProviderLease is returned before the identity that created it goes away.
                await final.DisposeAsync();
                throw;
            }
        } catch {
            await bootstrap.DisposeAsync();
            throw;
        }
    }

    #endregion

    /// <summary>
    ///     Combines the final turn scope and its bootstrap scope into the single disposable unit the
    ///     turn dispatcher holds, releasing the final scope — and, through it, the tenant provider
    ///     lease — before the bootstrap scope.
    /// </summary>
    private sealed class TwoPhaseScope(AsyncServiceScope final, AsyncServiceScope bootstrap) : IServiceScope, IAsyncDisposable
    {
        #region IServiceScope Members

        public IServiceProvider ServiceProvider => final.ServiceProvider;

        public void Dispose() {
            try {
                final.Dispose();
            } finally {
                bootstrap.Dispose();
            }
        }

        #endregion

        #region IAsyncDisposable Members

        public async ValueTask DisposeAsync() {
            try {
                await final.DisposeAsync();
            } finally {
                await bootstrap.DisposeAsync();
            }
        }

        #endregion
    }
}
