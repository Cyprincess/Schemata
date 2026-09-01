using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Exceptions;
using Schemata.Messaging.Skeleton;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Messaging;

/// <summary>
///     <see cref="IMessageContextPropagator" /> for the current tenant: <see cref="Capture" /> reads
///     the resolved tenant off <see cref="ITenantContextAccessor{TTenant}" /> in the sending scope,
///     and <see cref="RestoreAsync" /> reloads that tenant and reinitializes
///     <see cref="ITenantContextInitializer{TTenant}" /> in the receiving scope. Without this, a
///     scope built across a boundary (an actor turn, a RabbitMQ consumer) has an empty tenant
///     context and its repositories resolve against the wrong tenant provider — a silent
///     cross-tenant write.
/// </summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
public sealed class TenantMessageContextPropagator<TTenant> : IMessageContextPropagator
    where TTenant : SchemataTenant
{
    /// <summary>The <see cref="MessageContext.Items" /> key carrying the tenant's <see cref="SchemataTenant.Uid" />.</summary>
    private const string TenantIdKey = "tenancy.tenant-id";

    #region IMessageContextPropagator Members

    public void Capture(IDictionary<string, string?> items, IServiceProvider source) {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(source);

        var accessor = source.GetRequiredService<ITenantContextAccessor<TTenant>>();
        if (accessor.Tenant is { } tenant) {
            items[TenantIdKey] = tenant.Uid.ToString("D");
        }
    }

    public async ValueTask RestoreAsync(
        IReadOnlyDictionary<string, string?> items, IServiceProvider target, CancellationToken ct = default
    ) {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(target);

        if (!items.TryGetValue(TenantIdKey, out var raw) || string.IsNullOrEmpty(raw) || !Guid.TryParse(raw, out var tenantId)) {
            return;
        }

        var manager = target.GetRequiredService<ITenantManager<TTenant>>();
        var tenant  = await manager.FindByTenantId(tenantId, ct);
        if (tenant is null) {
            throw new TenantResolveException();
        }

        var initializer = target.GetRequiredService<ITenantContextInitializer<TTenant>>();
        await initializer.InitializeAsync(tenant, ct);
    }

    #endregion
}
